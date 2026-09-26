using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace ReconciliationAutomation
{
    /// <summary>Why reading an input document failed. Messages name the side and a position, never any of the data.</summary>
    internal sealed class InputFailure
    {
        internal InputFailure(string code, string message)
        {
            Code = code;
            Message = message;
        }

        internal string Code { get; }
        internal string Message { get; }
    }

    /// <summary>
    /// One side's input: a parsed top-level JSON array. Parsing is bounded (characters, depth, rows), rejects duplicate
    /// property names anywhere, and disallows comments and trailing commas. Failure messages are built from positions and
    /// limits only, because the exception text of a JSON parser can echo the offending data.
    /// </summary>
    internal sealed class JsonInput : IRowSource, IDisposable
    {
        internal const int MaxDepth = 64;

        /// <summary>The longest number token accepted, in characters. It bounds the memory one number can hold and any later conversion of it.</summary>
        internal const int MaxNumberTokenLength = 256;

        private readonly JsonDocument document;
        private readonly List<JsonElement> rows;

        private JsonInput(JsonDocument document, List<JsonElement> rows)
        {
            this.document = document;
            this.rows = rows;
        }

        /// <summary>The number of array items (rows), including items that are not objects.</summary>
        public int RowCount => rows.Count;

        /// <summary>A reader for the row at a zero-based source index.</summary>
        public IRowReader RowAt(int index) => new JsonRowReader(rows[index]);

        public void Dispose() => document.Dispose();

        internal static bool TryParse(string json, string side, ReconciliationLimits limits, out JsonInput input, out InputFailure failure)
        {
            input = null;
            failure = null;

            if (json == null)
            {
                failure = new InputFailure("MissingInput", side + " input is required (use [] for an empty dataset).");
                return false;
            }
            if (string.IsNullOrWhiteSpace(json))
            {
                failure = new InputFailure("MalformedJson", side + " input is empty; use [] for an empty dataset.");
                return false;
            }
            if (json.Length > limits.MaximumInputCharactersPerSide)
            {
                failure = new InputFailure("InputTooLarge", side + " input is longer than the limit of " + limits.MaximumInputCharactersPerSide + " characters.");
                return false;
            }

            if (TextCheck.HasUnpairedSurrogate(json))
            {
                failure = new InputFailure("InvalidText", side + " input contains text that is not valid (an unpaired surrogate character).");
                return false;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            if (!Scan(bytes, side, limits, out failure)) return false;

            JsonDocument document = null;
            try
            {
                document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = MaxDepth });
                var rows = new List<JsonElement>();
                foreach (JsonElement item in document.RootElement.EnumerateArray()) rows.Add(item);
                input = new JsonInput(document, rows);
                document = null;
                return true;
            }
            catch (JsonException ex)
            {
                // The scan already accepted this text, so this is unexpected; report the position only.
                failure = new InputFailure("MalformedJson", side + " input is not valid JSON" + Position(ex) + ".");
                return false;
            }
            finally { document?.Dispose(); }
        }

        /// <summary>
        /// One streaming pass that validates the document, counts the rows and rejects duplicate property names, before any
        /// tree is built.
        /// </summary>
        private static bool Scan(byte[] bytes, string side, ReconciliationLimits limits, out InputFailure failure)
        {
            failure = null;
            var names = new Stack<HashSet<string>>();
            int rows = 0;
            bool sawRoot = false;
            // One level of slack in the reader so that this scan, not the parser, reports the depth limit. Declared outside the try so a
            // failure can still say where the reader was.
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = MaxDepth + 1, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            try
            {
                while (reader.Read())
                {
                    if (!sawRoot)
                    {
                        sawRoot = true;
                        if (reader.TokenType != JsonTokenType.StartArray)
                        {
                            failure = new InputFailure("NotAnArray", side + " input must be a JSON array of objects.");
                            return false;
                        }
                    }

                    // Items of the top-level array sit at depth 1 (the array's own start token is depth 0).
                    if (reader.CurrentDepth == 1 && reader.TokenType != JsonTokenType.EndObject && reader.TokenType != JsonTokenType.EndArray && reader.TokenType != JsonTokenType.PropertyName)
                    {
                        if (++rows > limits.MaximumRowsPerSide)
                        {
                            failure = new InputFailure("TooManyRows", side + " input has more rows than the limit of " + limits.MaximumRowsPerSide + ".");
                            return false;
                        }
                    }

                    if ((reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray) && reader.CurrentDepth + 1 > MaxDepth)
                    {
                        failure = new InputFailure("DepthLimit", side + " input is nested deeper than the limit of " + MaxDepth + ".");
                        return false;
                    }

                    if (reader.TokenType == JsonTokenType.Number && reader.ValueSpan.Length > MaxNumberTokenLength)
                    {
                        failure = new InputFailure("NumberTooLong", side + " input has a number longer than " + MaxNumberTokenLength + " characters" + Position(reader) + ".");
                        return false;
                    }

                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject:
                            names.Push(new HashSet<string>(StringComparer.Ordinal));
                            break;
                        case JsonTokenType.EndObject:
                            names.Pop();
                            break;
                        case JsonTokenType.PropertyName:
                            if (!names.Peek().Add(reader.GetString()))
                            {
                                failure = new InputFailure("DuplicateProperty", side + " input has an object with a repeated property name" + Position(reader) + ".");
                                return false;
                            }
                            break;
                    }
                }
                if (!sawRoot)
                {
                    failure = new InputFailure("MalformedJson", side + " input is empty; use [] for an empty dataset.");
                    return false;
                }
                return true;
            }
            catch (JsonException ex)
            {
                failure = new InputFailure("MalformedJson", side + " input is not valid JSON" + Position(ex) + ".");
                return false;
            }
            catch (InvalidOperationException)
            {
                // A property name that is not valid text (an escaped lone surrogate such as \uD800) cannot be compared or looked up.
                failure = new InputFailure("InvalidText", side + " input has a property name that is not valid text (an unpaired surrogate escape)" + Position(reader) + ".");
                return false;
            }
        }

        private static string Position(JsonException ex) =>
            ex.LineNumber.HasValue && ex.BytePositionInLine.HasValue ? " (line " + (ex.LineNumber.Value + 1) + ", byte " + (ex.BytePositionInLine.Value + 1) + ")" : string.Empty;

        private static string Position(Utf8JsonReader reader) => " (byte offset " + reader.TokenStartIndex + ")";
    }
}
