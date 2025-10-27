// PromptUtils.cs
using System.Collections.Generic;
using System.Linq;

namespace Comfizen
{
    /// <summary>
    /// Provides utility methods for parsing and manipulating prompt strings.
    /// This class serves as a single source of truth for tokenization logic.
    /// </summary>
    public static class PromptUtils
    {
        /// <summary>
        /// The prefix character used to mark a token as disabled.
        /// </summary>
        public const string DISABLED_TOKEN_PREFIX = "\uD83D\uDD12";

        /// <summary>
        /// Splits a prompt string into a list of tokens, respecting brackets and parentheses.
        /// This is the primary tokenization logic used throughout the application.
        /// </summary>
        /// <param name="str">The prompt string to tokenize.</param>
        /// <returns>A list of individual tokens.</returns>
        public static List<string> Tokenize(string str)
        {
            var tokens = new List<string>();
            if (string.IsNullOrEmpty(str)) return tokens;

            // Replace newlines with commas to treat them as delimiters.
            var processedStr = str.Replace('\r', ',').Replace('\n', ',');
            var currentToken = "";
            int insideBrackets = 0;
            int insideParentheses = 0;

            foreach (char c in processedStr)
            {
                if (c == '{') { insideBrackets++; currentToken += c; }
                else if (c == '}') { insideBrackets--; currentToken += c; }
                else if (c == '(') { insideParentheses++; currentToken += c; }
                else if (c == ')') { insideParentheses--; currentToken += c; }
                else if (c == ',' && insideBrackets == 0 && insideParentheses == 0)
                {
                    var trimmedToken = currentToken.Trim();
                    if (!string.IsNullOrEmpty(trimmedToken))
                    {
                        tokens.Add(trimmedToken);
                    }
                    currentToken = "";
                }
                else
                {
                    currentToken += c;
                }
            }
            
            var finalTrimmedToken = currentToken.Trim();
            if (!string.IsNullOrEmpty(finalTrimmedToken))
            {
                tokens.Add(finalTrimmedToken);
            }
            
            return tokens;
        }
    }
}