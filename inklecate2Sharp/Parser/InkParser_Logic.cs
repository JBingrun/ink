﻿using System;
using System.Collections.Generic;
using System.Linq;
using Inklewriter.Parsed;

namespace Inklewriter
{
    public partial class InkParser
    {
        
        protected Parsed.Object LogicLine()
        {
            BeginRule ();

            Whitespace ();

            if (ParseString ("~") == null) {
                return FailRule () as Parsed.Object;
            }

            Whitespace ();

            // Some example lines we need to be able to distinguish between:
            // ~ var x = 5  -- var decl + assign
            // ~ var x      -- var decl
            // ~ x = 5      -- var assign
            // ~ x          -- expr (not var decl or assign)
            // ~ f()        -- expr
            // We don't treat variable decl/assign as an expression since we don't want an assignment
            // to have a return value, or to be used in compound expressions.
            ParseRule afterTilda = () => OneOf (IncludeStatement, ReturnStatement, VariableDeclarationOrAssignment, Expression);

            var parsedExpr = (Parsed.Object) Expect(afterTilda, "expression after '~'", recoveryRule: SkipToNextLine);

            // TODO: A piece of logic after a tilda shouldn't have its result printed as text (I don't think?)
            return SucceedRule (parsedExpr) as Parsed.Object;
        }

        protected object IncludeStatement()
        {
            BeginRule ();

            if (ParseString ("include") == null) {
                return (IncludedFile)FailRule ();
            }

            Whitespace ();

            var filename = (string) Expect(() => ParseUntilCharactersFromString ("\n\r"), "filename for include statement");
            filename = filename.TrimEnd (' ', '\t');

            Parsed.Story includedStory = null;
            try {
                string includedString = System.IO.File.ReadAllText(filename);

                InkParser parser = new InkParser(includedString, filename);
                includedStory = parser.Parse();

                if( includedStory == null ) {
                    // This error should never happen: if the includedStory isn't
                    // returned, then it should've been due to some error that
                    // has already been reported, so this is a last resort.
                    if( !parser.hadError ) {
                        Error ("Failed to parse included file '" + filename + "'");
                    }
                }

            }
            catch {
                Error ("Included file not found: " + filename);
            }

            // Succeed even when story failed to parse and we have a null story:
            //  we don't want to attempt to re-parse the include line as something else
            var includedFile = new IncludedFile (includedStory);
            return SucceedRule (includedFile);
        }

        protected List<Parsed.Object> LineOfMixedTextAndLogic()
        {
            BeginRule ();

            var result = MixedTextAndLogic();
            if (result == null || result.Count == 0) {
                return (List<Parsed.Object>) FailRule();
            }

            // Trim whitepace from start
            var firstText = result[0] as Text;
            if (firstText != null) {
                firstText.text = firstText.text.TrimStart(' ', '\t');
                if (firstText.text.Length == 0) {
                    result.RemoveAt (0);
                }
            }
            if (result.Count == 0) {
                return (List<Parsed.Object>) FailRule();
            }

            // Trim whitespace from end and add a newline
            var lastObj = result.Last ();
            if (lastObj is Text) {
                var text = (Text)lastObj;
                text.text = text.text.TrimEnd (' ', '\t') + "\n";
            } 

            // Last object in line wasn't text (but some kind of logic), so
            // we need to append the newline afterwards using a new object
            // If we end up generating multiple newlines (e.g. due to conditional
            // logic), we rely on the runtime to absorb them.
            // TODO: Is there some more clever logic we can do here?
            else {
                result.Add (new Text ("\n"));
            }

            Expect(EndOfLine, "end of line", recoveryRule: SkipToNextLine);

            return (List<Parsed.Object>) SucceedRule(result);
        }

        protected List<Parsed.Object> MixedTextAndLogic()
        {
            // Either, or both interleaved
            return Interleave<Parsed.Object>(Optional (ContentText), Optional (InlineLogicOrGlue));
        }

        protected Parsed.Object InlineLogicOrGlue()
        {
            return (Parsed.Object) OneOf (InlineLogic, Glue);
        }

        protected Parsed.Wrap<Runtime.Glue> Glue()
        {
            // Don't want to parse whitespace, since it might be important
            // surrounding the glue.
            var glueStr = ParseString("::");
            if (glueStr != null) {
                var glue = new Runtime.Glue ();
                return new Parsed.Wrap<Runtime.Glue> (glue);
            } else {
                return null;
            }
        }

        protected Parsed.Object InlineLogic()
        {
            BeginRule ();

            if ( ParseString ("{") == null) {
                return FailRule () as Parsed.Object;
            }

            Whitespace ();

            var logic = InnerLogic ();
            if (logic == null) {
                return (Parsed.Object) FailRule ();
            }
                
            Whitespace ();

            Expect (String("}"), "closing brace '}' for inline logic");

            return SucceedRule(logic) as Parsed.Object;
        }

        protected Parsed.Object ExpectedInnerLogic()
        {
            var innerLogicObj = Expect(() => OneOf (
                InnerConditionalContent, 
                InnerSequence,
                InnerExpression), 
                "inner logic or sequence between '{' and '}' braces");

            return (Parsed.Object) innerLogicObj;
        }

        protected Parsed.Object InnerLogic()
        {
            ParseRule[] rules = {
                InnerConditionalContent, 
                InnerExpression,
                InnerSequence
            };

            // Adapted from "OneOf" structuring rule except that in 
            // order for the rule to succeed, it has to maximally 
            // cover the entire string within the { }. Used to
            // differentiate between:
            //  {myVar}                 -- Expression (try first)
            //  {my content is jolly}   -- sequence with single element
            foreach (ParseRule rule in rules) {
                BeginRule ();

                Parsed.Object result = rule () as Parsed.Object;
                if (result != null) {

                    // Not yet at end?
                    if (Peek (Spaced (String ("}"))) == null)
                        FailRule ();

                    // Full parse of content within braces
                    else
                        return (Parsed.Object) SucceedRule (result);
                    
                } else {
                    FailRule ();
                }
            }

            return null;
        }

        protected Parsed.Object InnerExpression()
        {
            var expr = Expression ();
            if (expr != null) {
                expr.outputWhenComplete = true;
            }
            return expr;
        }

        protected Sequence InnerSequence()
        {
            var listOfLists = Interleave<List<Parsed.Object>> (Optional (MixedTextAndLogic), Exclude(String ("|")), flatten:false);
            if (listOfLists == null) {
                return (Sequence) FailRule ();
            }

            var seq = new Sequence (listOfLists);
            return (Sequence) SucceedRule (seq);
        }
    }
}

