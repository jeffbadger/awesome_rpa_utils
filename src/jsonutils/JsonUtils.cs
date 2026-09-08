using System;
using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace JsonAutomation
{
    /// <summary>
    /// A Pega Robot Studio-ready component for reading, updating, validating, and
    /// transforming JSON via real JSONPath (Newtonsoft.Json's <c>SelectToken</c>/
    /// <c>SelectTokens</c>), as a full replacement for the native <c>Json</c> component's
    /// dot-notation-only path support. Like every component in this suite, its methods
    /// report recoverable failures as <c>False</c> with a descriptive message instead of
    /// throwing.
    /// </summary>
    [Description("Reads, updates, validates, and transforms JSON via JSONPath. " +
                 "All methods return True/False with a failure message instead of throwing. " +
                 "Drag this component onto a Pega Robot Studio automation to use its methods.")]
    public class JsonUtils : Component
    {
        /// <summary>
        /// Empty constructor required so Pega Robot Studio can create the component.
        /// </summary>
        public JsonUtils()
        {
        }

        /// <summary>
        /// Standard designer constructor; attaches the component to a container.
        /// </summary>
        /// <param name="container">The designer container to add this component to. May be null.</param>
        public JsonUtils(IContainer container)
        {
            container?.Add(this);
        }

        #region Native parity

        /// <summary>Deserializes a JSON string into an instance of the given .NET type.
        /// <paramref name="typeName"/> should be a design-time-authored literal the automation's
        /// author wires in - the same trust model as any other Robot Studio canvas string
        /// parameter - not a value populated from untrusted runtime/external data.</summary>
        /// <param name="json">The JSON text to deserialize.</param>
        /// <param name="typeName">An assembly-qualified or in-scope simple type name, resolved via <see cref="Type.GetType(string)"/>.</param>
        /// <param name="result">The deserialized object on success; <c>null</c> on failure.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if deserialization succeeded.</returns>
        [Category("Json - Core")]
        [Description("Deserializes a JSON string into an instance of the named .NET type. Never throws.")]
        public bool TryDeserializeObject(string json, string typeName, out object result, out string message)
        {
            result = null;
            message = null;
            try
            {
                Type type = Type.GetType(typeName);
                if (type == null)
                {
                    message = $"Type '{typeName}' could not be resolved.";
                    return false;
                }
                result = JsonConvert.DeserializeObject(json, type, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
                return true;
            }
            catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
            {
                message = NeverThrowsGuard.Failure(nameof(TryDeserializeObject), exception);
                return false;
            }
        }

        /// <summary>Serializes an object to a JSON string.</summary>
        /// <param name="value">The object to serialize.</param>
        /// <param name="json">The JSON text on success; <c>null</c> on failure.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if serialization succeeded.</returns>
        [Category("Json - Core")]
        [Description("Serializes an object to a JSON string. Never throws.")]
        public bool TrySerializeObject(object value, out string json, out string message)
        {
            json = null;
            message = null;
            try
            {
                json = JsonConvert.SerializeObject(value);
                return true;
            }
            catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
            {
                message = NeverThrowsGuard.Failure(nameof(TrySerializeObject), exception);
                return false;
            }
        }

        /// <summary>Extracts a single value from a JSON string using a JSONPath expression.</summary>
        /// <param name="json">The JSON text to read.</param>
        /// <param name="path">A JSONPath expression, e.g. <c>order.items[0].sku</c>.</param>
        /// <param name="value">The value at <paramref name="path"/> as a string on success (or
        /// <c>null</c> if the value is a JSON null literal); <c>null</c> on failure.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if <paramref name="path"/> resolved to a value.</returns>
        [Category("Json - Core")]
        [Description("Extracts a single value from a JSON string using a JSONPath expression. Never throws.")]
        public bool TryGetValueFromJson(string json, string path, out string value, out string message)
        {
            value = null;
            message = null;
            try
            {
                JToken root = JToken.Parse(json);
                JToken token = root.SelectToken(path);
                if (token == null)
                {
                    message = $"Path '{path}' did not resolve to a value.";
                    return false;
                }
                value = token.Type == JTokenType.Null ? null : token.ToString();
                return true;
            }
            catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
            {
                message = NeverThrowsGuard.Failure(nameof(TryGetValueFromJson), exception);
                return false;
            }
        }

        /// <summary>Updates a value in a JSON string using a JSONPath expression. The path must
        /// already resolve to an existing value - this replaces a value in place, it does not
        /// create new object properties or array elements along the way.</summary>
        /// <param name="json">The JSON text to update.</param>
        /// <param name="path">A JSONPath expression identifying an existing value.</param>
        /// <param name="value">The new value, set as a JSON string scalar.</param>
        /// <param name="updatedJson">The updated JSON text on success; <c>null</c> on failure.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if <paramref name="path"/> resolved to an existing value that was updated.</returns>
        [Category("Json - Core")]
        [Description("Updates a value in a JSON string using a JSONPath expression. The path must already exist. Never throws.")]
        public bool TrySetValueInJson(string json, string path, string value, out string updatedJson, out string message)
        {
            updatedJson = null;
            message = null;
            try
            {
                JToken root = JToken.Parse(json);
                JToken target = root.SelectToken(path);
                if (target == null)
                {
                    message = $"Path '{path}' did not resolve to an existing value; TrySetValueInJson can only replace a value at a path that already exists.";
                    return false;
                }
                target.Replace(JToken.FromObject(value));
                updatedJson = root.ToString(Formatting.None);
                return true;
            }
            catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
            {
                message = NeverThrowsGuard.Failure(nameof(TrySetValueInJson), exception);
                return false;
            }
        }

        #endregion

        #region Validation and typed getters

        /// <summary>Checks whether a string is well-formed JSON.</summary>
        /// <param name="json">The text to check.</param>
        /// <param name="message"><c>null</c> if valid; a description of the parse failure otherwise.</param>
        /// <returns><c>True</c> if <paramref name="json"/> parses as JSON.</returns>
        [Category("Json - Validation")]
        [Description("Checks whether a string is well-formed JSON. Never throws.")]
        public bool IsValidJson(string json, out string message)
        {
            message = null;
            try
            {
                JToken.Parse(json);
                return true;
            }
            catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
            {
                message = NeverThrowsGuard.Failure(nameof(IsValidJson), exception);
                return false;
            }
        }

        private bool TryGetTypedValue<T>(string json, string path, string methodName, out T value, out string message)
        {
            value = default;
            message = null;
            try
            {
                JToken root = JToken.Parse(json);
                JToken token = root.SelectToken(path);
                if (token == null)
                {
                    message = $"Path '{path}' did not resolve to a value.";
                    return false;
                }
                value = token.Value<T>();
                return true;
            }
            catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
            {
                message = NeverThrowsGuard.Failure(methodName, exception);
                return false;
            }
        }

        /// <summary>Extracts a value at a JSONPath as a <see cref="string"/>.</summary>
        /// <param name="json">The JSON text to read.</param>
        /// <param name="path">A JSONPath expression.</param>
        /// <param name="value">The value on success; <c>null</c> on failure.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if <paramref name="path"/> resolved to a value convertible to <see cref="string"/>.</returns>
        [Category("Json - Validation")]
        [Description("Extracts a value at a JSONPath as a string. Never throws.")]
        public bool TryGetStringValue(string json, string path, out string value, out string message) =>
            TryGetTypedValue(json, path, nameof(TryGetStringValue), out value, out message);

        /// <summary>Extracts a value at a JSONPath as an <see cref="int"/>.</summary>
        /// <param name="json">The JSON text to read.</param>
        /// <param name="path">A JSONPath expression.</param>
        /// <param name="value">The value on success; <c>0</c> on failure.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if <paramref name="path"/> resolved to a value convertible to <see cref="int"/>.</returns>
        [Category("Json - Validation")]
        [Description("Extracts a value at a JSONPath as an int. Never throws.")]
        public bool TryGetIntValue(string json, string path, out int value, out string message) =>
            TryGetTypedValue(json, path, nameof(TryGetIntValue), out value, out message);

        /// <summary>Extracts a value at a JSONPath as a <see cref="bool"/>.</summary>
        /// <param name="json">The JSON text to read.</param>
        /// <param name="path">A JSONPath expression.</param>
        /// <param name="value">The value on success; <c>false</c> on failure.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if <paramref name="path"/> resolved to a value convertible to <see cref="bool"/>.</returns>
        [Category("Json - Validation")]
        [Description("Extracts a value at a JSONPath as a bool. Never throws.")]
        public bool TryGetBoolValue(string json, string path, out bool value, out string message) =>
            TryGetTypedValue(json, path, nameof(TryGetBoolValue), out value, out message);

        /// <summary>Extracts a value at a JSONPath as a <see cref="double"/>.</summary>
        /// <param name="json">The JSON text to read.</param>
        /// <param name="path">A JSONPath expression.</param>
        /// <param name="value">The value on success; <c>0</c> on failure.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if <paramref name="path"/> resolved to a value convertible to <see cref="double"/>.</returns>
        [Category("Json - Validation")]
        [Description("Extracts a value at a JSONPath as a double. Never throws.")]
        public bool TryGetDoubleValue(string json, string path, out double value, out string message) =>
            TryGetTypedValue(json, path, nameof(TryGetDoubleValue), out value, out message);

        /// <summary>Extracts a value at a JSONPath as a <see cref="DateTime"/>.</summary>
        /// <param name="json">The JSON text to read.</param>
        /// <param name="path">A JSONPath expression.</param>
        /// <param name="value">The value on success; <see cref="DateTime.MinValue"/> on failure.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if <paramref name="path"/> resolved to a value convertible to <see cref="DateTime"/>.</returns>
        [Category("Json - Validation")]
        [Description("Extracts a value at a JSONPath as a DateTime. Never throws.")]
        public bool TryGetDateTimeValue(string json, string path, out DateTime value, out string message) =>
            TryGetTypedValue(json, path, nameof(TryGetDateTimeValue), out value, out message);

        #endregion

        #region Multi-match and inspection

        /// <summary>Extracts every value matching a JSONPath (e.g. a wildcard or filter
        /// expression) and joins them into one delimited string.</summary>
        /// <param name="json">The JSON text to read.</param>
        /// <param name="path">A JSONPath expression that may match zero or more values.</param>
        /// <param name="delimiter">The delimiter to join matched values with.</param>
        /// <param name="delimitedValues">The joined values on success; <c>null</c> on failure.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if <paramref name="path"/> matched at least one value.</returns>
        [Category("Json - Query")]
        [Description("Extracts every value matching a JSONPath and joins them into one delimited string. Never throws.")]
        public bool TryGetValuesFromJson(string json, string path, string delimiter, out string delimitedValues, out string message)
        {
            delimitedValues = null;
            message = null;
            try
            {
                JToken root = JToken.Parse(json);
                List<string> values = new List<string>();
                foreach (JToken token in root.SelectTokens(path))
                {
                    values.Add(token.Type == JTokenType.Null ? string.Empty : token.ToString());
                }
                if (values.Count == 0)
                {
                    message = $"Path '{path}' did not match any values.";
                    return false;
                }
                delimitedValues = string.Join(delimiter, values);
                return true;
            }
            catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
            {
                message = NeverThrowsGuard.Failure(nameof(TryGetValuesFromJson), exception);
                return false;
            }
        }

        /// <summary>Reports the kind of value found at a JSONPath.</summary>
        /// <param name="json">The JSON text to read.</param>
        /// <param name="path">A JSONPath expression.</param>
        /// <param name="kind">The value's kind on success; <see cref="JsonValueKind.NotFound"/> on failure.</param>
        /// <param name="message"><c>null</c> on success; a description of the failure otherwise.</param>
        /// <returns><c>True</c> if <paramref name="path"/> resolved to a value.</returns>
        [Category("Json - Query")]
        [Description("Reports the kind of value found at a JSONPath. Never throws.")]
        public bool TryGetValueType(string json, string path, out JsonValueKind kind, out string message)
        {
            kind = JsonValueKind.NotFound;
            message = null;
            try
            {
                JToken root = JToken.Parse(json);
                JToken token = root.SelectToken(path);
                if (token == null)
                {
                    message = $"Path '{path}' did not resolve to a value.";
                    return false;
                }
                kind = token.Type switch
                {
                    JTokenType.Null => JsonValueKind.Null,
                    JTokenType.String => JsonValueKind.String,
                    JTokenType.Integer => JsonValueKind.Number,
                    JTokenType.Float => JsonValueKind.Number,
                    JTokenType.Boolean => JsonValueKind.Boolean,
                    JTokenType.Array => JsonValueKind.Array,
                    JTokenType.Object => JsonValueKind.Object,
                    _ => JsonValueKind.String
                };
                return true;
            }
            catch (Exception exception) when (NeverThrowsGuard.IsRecoverable(exception))
            {
                message = NeverThrowsGuard.Failure(nameof(TryGetValueType), exception);
                return false;
            }
        }

        #endregion

        #region Array and removal

        #endregion

        #region Formatting

        #endregion
    }
}
