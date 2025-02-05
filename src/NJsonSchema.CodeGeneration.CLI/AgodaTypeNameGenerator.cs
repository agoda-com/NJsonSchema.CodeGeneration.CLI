﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NJsonSchema.CodeGeneration.CLI.Console
{
    internal class AgodaTypeNameGenerator : ITypeNameGenerator
    {

        /// <summary>Gets or sets the reserved names.</summary>
        public IEnumerable<string> ReservedTypeNames { get; set; } = new List<string> { "object" };

        /// <summary>Gets the name mappings.</summary>
        public IDictionary<string, string> TypeNameMappings { get; } = new Dictionary<string, string>();

        /// <summary>Generates the type name for the given schema respecting the reserved type names.</summary>
        /// <param name="schema">The schema.</param>
        /// <param name="typeNameHint">The type name hint.</param>
        /// <param name="reservedTypeNames">The reserved type names.</param>
        /// <returns>The type name.</returns>
        public virtual string Generate(JsonSchema schema, string typeNameHint, IEnumerable<string> reservedTypeNames)
        {
            if (string.IsNullOrEmpty(typeNameHint) && !string.IsNullOrEmpty(schema.DocumentPath))
            {
                typeNameHint = schema.DocumentPath.Replace("\\", "/").Split('/').Last();
            }

            typeNameHint = (typeNameHint ?? "")
                .Replace("[", " Of ")
                .Replace("]", " ")
                .Replace("<", " Of ")
                .Replace(">", " ")
                .Replace(",", " And ")
                .Replace("  ", " ");

            var parts = typeNameHint.Split(' ');
            typeNameHint = string.Join(string.Empty, parts.Select(p => Generate(schema, p)));

            var typeName = Generate(schema, typeNameHint);
            if (string.IsNullOrEmpty(typeName) || reservedTypeNames.Contains(typeName))
            {
                typeName = GenerateAnonymousTypeName(typeNameHint, reservedTypeNames);
            }
            
            return RemoveIllegalCharacters(typeName).SnakeToPascal();
        }

        /// <summary>Generates the type name for the given schema.</summary>
        /// <param name="schema">The schema.</param>
        /// <param name="typeNameHint">The type name hint.</param>
        /// <returns>The type name.</returns>
        protected virtual string Generate(JsonSchema schema, string typeNameHint)
        {
            if (string.IsNullOrEmpty(typeNameHint) && schema.HasTypeNameTitle)
            {
                typeNameHint = schema.Title;
            }

            var lastSegment = typeNameHint?.Split('.').Last();
            return ConversionUtilities.ConvertToUpperCamelCase(lastSegment ?? "Anonymous", true);
        }

        private string GenerateAnonymousTypeName(string typeNameHint, IEnumerable<string> reservedTypeNames)
        {
            if (!string.IsNullOrEmpty(typeNameHint))
            {
                if (TypeNameMappings.ContainsKey(typeNameHint))
                {
                    typeNameHint = TypeNameMappings[typeNameHint];
                }

                typeNameHint = typeNameHint.Split('.').Last();

                if (!reservedTypeNames.Contains(typeNameHint) && !ReservedTypeNames.Contains(typeNameHint))
                {
                    return typeNameHint;
                }

                var count = 1;
                do
                {
                    count++;
                } while (reservedTypeNames.Contains(typeNameHint + count));

                return typeNameHint + count;
            }

            return GenerateAnonymousTypeName("Anonymous", reservedTypeNames);
        }

        /// <summary>
        /// Replaces all characters that are not normals letters, numbers or underscore, with an underscore.
        /// Will prepend an underscore if the first characters is a number.
        /// In case there are this would result in multiple underscores in a row, strips down to one underscore.
        /// Will trim any underscores at the end of the type name.
        /// </summary>
        private string RemoveIllegalCharacters(string typeName)
        {
            // TODO: Find a way to support unicode characters up to 3.0
            var legalTypeName = new StringBuilder(typeName);

            var firstCharacter = legalTypeName[0].ToString();
            var regexValidStartChar = new Regex("[a-zA-Z_]");
            var regexInvalidCharacters = new Regex("\\W");

            if (!regexValidStartChar.IsMatch(firstCharacter))
            {
                if (!regexInvalidCharacters.IsMatch(firstCharacter))
                {
                    legalTypeName.Insert(0, "_");
                }
                else
                {
                    legalTypeName[0] = '_';
                }
            }

            var illegalMatches = regexInvalidCharacters.Matches(legalTypeName.ToString());

            for (int i = illegalMatches.Count - 1; i >= 0; i--)
            {
                var illegalMatchIndex = illegalMatches[i].Index;
                legalTypeName[illegalMatchIndex] = '_';
            }

            var regexMoreThanOneUnderscore = new Regex("[_]{2,}");

            var legalTypeNameString = regexMoreThanOneUnderscore.Replace(legalTypeName.ToString(), "_");
            return legalTypeNameString.TrimEnd('_');
        }
    }
}