using NJsonSchema.CodeGeneration;
using NJsonSchema;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Customization
{
    public sealed class PascalCasePropertyNameGenerator : IPropertyNameGenerator
    {
        private static readonly char[] _reservedFirstPassChars = ['"', '\'', '@', '?', '!', '$', '[', ']', '(', ')', '.', '=', '+', '|'];
        private static readonly char[] _reservedSecondPassChars = ['*', ':', '-', '#', '&'];

        /// <summary>Generates the property name.</summary>
        /// <param name="property">The property.</param>
        /// <returns>The new name.</returns>
        public string Generate(JsonSchemaProperty property)
        {
            var name = property.Name;

            if (name.IndexOfAny(_reservedFirstPassChars) != -1)
            {
                name = name.Replace("\"", string.Empty)
                    .Replace("'", string.Empty)
                    .Replace("@", string.Empty)
                    .Replace("?", string.Empty)
                    .Replace("!", string.Empty)
                    .Replace("$", string.Empty)
                    .Replace("[", string.Empty)
                    .Replace("]", string.Empty)
                    .Replace("(", "_")
                    .Replace(")", string.Empty)
                    .Replace(".", "-")
                    .Replace("=", "-")
                    .Replace("+", "plus")
                    .Replace("|", "_");
            }

            name = ConversionUtilities.ConvertToUpperCamelCase(name, true);

            if (name.IndexOfAny(_reservedSecondPassChars) != -1)
            {
                name = name
                    .Replace("*", "Star")
                    .Replace(":", "_")
                    .Replace("-", "_")
                    .Replace("#", "_")
                    .Replace("&", "And");
            }

            name = ConvertToPascalCase(name);

            return name;
        }

        public static string ConvertToPascalCase(string input)
        {
            var result = new StringBuilder();
            var capitalizeNext = false;

            foreach (char c in input)
            {
                if (c == '_' || c == '-')
                {
                    capitalizeNext = true;
                }
                else
                {
                    if (capitalizeNext)
                    {
                        result.Append(char.ToUpper(c));
                        capitalizeNext = false;
                    }
                    else
                    {
                        result.Append(c);
                    }
                }
            }

            var str = result.ToString();
            return UpperFirst(str);
        }

        public static string UpperFirst(string str)
        {
            return str.Substring(0, 1).ToUpper() + str.Substring(1);
        }
    }
}
