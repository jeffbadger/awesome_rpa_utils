using System;
using System.Collections.Generic;
using System.IO;
using OcrAutomation;

namespace Exerciser.Menus
{
    /// <summary>
    /// OcrUtils' menu. Region-based methods need real screen coordinates -
    /// point them at the checked-in fixture image displayed on screen, or at
    /// any visible text. GetTextFromImageFile defaults straight to the fixture
    /// file, no screen needed. Simple-suffixed wrapper overloads are omitted;
    /// their non-Simple siblings cover the same ground with more output.
    /// </summary>
    internal static class OcrMenu
    {
        internal static MenuItem[] Build(OcrUtils ocr)
        {
            return new[]
            {
                new MenuItem("GetTextFromRegion", "Screen region in pixels - point it at visible text on screen.", () =>
                {
                    int left = Prompt.Int("Left", 0);
                    int top = Prompt.Int("Top", 0);
                    int width = Prompt.Int("Width", 400);
                    int height = Prompt.Int("Height", 120);
                    string languageTag = Prompt.String("Language tag (blank for default)", null);
                    bool ok = ocr.GetTextFromRegion(left, top, width, height, out string text, out string message, languageTag);
                    Report.Result(ok, message, ("text", text));
                }),
                new MenuItem("GetTextFromImageFile", "Defaults to the checked-in fixture image (known text: \"Hello Exerciser\").", () =>
                {
                    string defaultPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ocr-sample.png");
                    string filePath = Prompt.String("Image file path", defaultPath);
                    string languageTag = Prompt.String("Language tag (blank for default)", null);
                    bool ok = ocr.GetTextFromImageFile(filePath, out string text, out string message, languageTag);
                    Report.Result(ok, message, ("text", text));
                }),
                new MenuItem("GetStructuredTextFromRegionAsJson", "Screen region in pixels; returns positioned words/lines as JSON.", () =>
                {
                    int left = Prompt.Int("Left", 0);
                    int top = Prompt.Int("Top", 0);
                    int width = Prompt.Int("Width", 400);
                    int height = Prompt.Int("Height", 120);
                    string languageTag = Prompt.String("Language tag (blank for default)", null);
                    bool ok = ocr.GetStructuredTextFromRegionAsJson(left, top, width, height, out string json, out string message, languageTag);
                    Report.Result(ok, message, ("json", json));
                }),
                new MenuItem("FindTextLocation", "Screen region in pixels; searches for searchText within it.", () =>
                {
                    string searchText = Prompt.String("Search text", "Hello");
                    int left = Prompt.Int("Left", 0);
                    int top = Prompt.Int("Top", 0);
                    int width = Prompt.Int("Width", 400);
                    int height = Prompt.Int("Height", 120);
                    bool ok = ocr.FindTextLocation(searchText, left, top, width, height, out int foundLeft, out int foundTop, out int foundWidth, out int foundHeight, out string message);
                    Report.Result(ok, message, ("foundLeft", foundLeft), ("foundTop", foundTop), ("foundWidth", foundWidth), ("foundHeight", foundHeight));
                }),
                new MenuItem("TryGetAvailableLanguages", "No input. Lists installed Windows OCR language packs - depends on the host, see the Setup/Cleanup menu.", () =>
                {
                    bool ok = ocr.TryGetAvailableLanguages(out List<string> tags, out string message);
                    Report.Result(ok, message, ("tags", tags));
                }),
                new MenuItem("GetAvailableLanguagesDelimited", "No input.", () =>
                {
                    string delimiter = Prompt.String("Delimiter", ",");
                    bool ok = ocr.GetAvailableLanguagesDelimited(out string tags, out string message, delimiter);
                    Report.Result(ok, message, ("tags", tags));
                }),
                new MenuItem("WaitForTextToAppear", "Found-in-time case: make the expected text appear on screen partway through the wait.", () =>
                {
                    int left = Prompt.Int("Left", 0);
                    int top = Prompt.Int("Top", 0);
                    int width = Prompt.Int("Width", 400);
                    int height = Prompt.Int("Height", 120);
                    string expectedText = Prompt.String("Expected text", "Hello");
                    int timeoutMs = Prompt.Int("Timeout ms", 10000);
                    int pollIntervalMs = Prompt.Int("Poll interval ms", 500);
                    bool ok = ocr.WaitForTextToAppear(left, top, width, height, expectedText, timeoutMs, pollIntervalMs, out bool timedOut, out string message);
                    Report.Result(ok, message, ("timedOut", timedOut));
                })
            };
        }
    }
}
