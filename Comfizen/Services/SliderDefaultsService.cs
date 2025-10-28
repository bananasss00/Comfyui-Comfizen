using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Comfizen
{
    /// <summary>
    /// Represents a parsed rule for a slider's default values.
    /// </summary>
    public class SliderDefaultRule
    {
        public string NodeType { get; set; } // Can be null
        public string FieldName { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Step { get; set; }
        public int? Precision { get; set; }
    }

    /// <summary>
    /// Manages parsing and retrieving default values for sliders based on user settings.
    /// </summary>
    public class SliderDefaultsService
    {
        private readonly List<SliderDefaultRule> _rulesWithNodeType = new List<SliderDefaultRule>();
        private readonly List<SliderDefaultRule> _rulesWithoutNodeType = new List<SliderDefaultRule>();

        public SliderDefaultsService(List<string> rules)
        {
            if (rules == null) return;

            foreach (var ruleString in rules)
            {
                var rule = ParseRule(ruleString);
                if (rule != null)
                {
                    if (string.IsNullOrEmpty(rule.NodeType))
                    {
                        _rulesWithoutNodeType.Add(rule);
                    }
                    else
                    {
                        _rulesWithNodeType.Add(rule);
                    }
                }
            }
        }

        /// <summary>
        /// Tries to find a matching default rule for a given field.
        /// Rules with a matching node type have higher priority.
        /// </summary>
        public bool TryGetDefaults(string nodeType, string fieldName, out SliderDefaultRule defaults)
        {
            // 1. High priority search: Match both NodeType and FieldName
            if (!string.IsNullOrEmpty(nodeType))
            {
                defaults = _rulesWithNodeType.FirstOrDefault(r =>
                    r.NodeType.Equals(nodeType, StringComparison.OrdinalIgnoreCase) &&
                    r.FieldName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

                if (defaults != null)
                {
                    return true;
                }
            }

            // 2. Low priority search: Match only FieldName
            defaults = _rulesWithoutNodeType.FirstOrDefault(r =>
                r.FieldName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

            return defaults != null;
        }

        private SliderDefaultRule ParseRule(string ruleString)
        {
            try
            {
                var parts = ruleString.Split(new[] { '=' }, 2);
                if (parts.Length != 2) return null;

                string key = parts[0].Trim();
                string valueString = parts[1].Trim();

                string nodeType = null;
                string fieldName = key;

                if (key.Contains("::"))
                {
                    var keyParts = key.Split(new[] { "::" }, 2, StringSplitOptions.None);
                    nodeType = keyParts[0];
                    fieldName = keyParts[1];
                }

                var valueParts = valueString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (valueParts.Length < 3 || valueParts.Length > 4) return null;

                var rule = new SliderDefaultRule
                {
                    NodeType = nodeType,
                    FieldName = fieldName,
                    Min = double.Parse(valueParts[0].Replace(',', '.'), CultureInfo.InvariantCulture),
                    Max = double.Parse(valueParts[1].Replace(',', '.'), CultureInfo.InvariantCulture),
                    Step = double.Parse(valueParts[2].Replace(',', '.'), CultureInfo.InvariantCulture),
                    Precision = valueParts.Length == 4 ? int.Parse(valueParts[3]) : (int?)null
                };

                return rule;
            }
            catch (Exception ex)
            {
                Logger.Log(ex, $"Failed to parse slider default rule: '{ruleString}'");
                return null;
            }
        }
    }
}