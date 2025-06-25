using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EasySave_G3_V1
{
    public class Langage
    {
        // ---------------------------------------------------------------------
        // Fields
        // ---------------------------------------------------------------------
        private string Title;                       // e.g. "French.json"
        private string Source;                      // full relative path to file
        private readonly Dictionary<string, string> Elements; // key => translated text

        // ---------------------------------------------------------------------
        // Constructors
        // ---------------------------------------------------------------------

        /// <summary>
        /// Default ctor – detects the desired language from *settings.json*.  
        /// If the file or key is missing, falls back to French.
        /// </summary>
        public Langage()
        {
            Elements = new Dictionary<string, string>();

            try
            {
                // 1. Read global settings
                using FileStream fs = File.OpenRead("settings.json");
                using JsonDocument doc = JsonDocument.Parse(fs);

                // 2. Extract “Langue” (expected value = file title w/out extension)
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("Langue", out JsonElement langueElement))
                {
                    Title = langueElement.GetString();
                    Source = $"Langages/{Title}.json";   // e.g. Langages/French.json
                }
                else
                {
                    // Fallback to French
                    Title = "Français";
                    Source = "Langages/Français.json";
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error reading settings.json: {e.Message}");
                Title = "Français";
                Source = "Langages/Français.json";
            }
        }

        /// <summary>
        /// Explicit ctor used by the UI / tests.
        /// </summary>
        public Langage(string title, string source)
        {
            Title = title;
            Source = source;
            Elements = new Dictionary<string, string>();
        }

        // ---------------------------------------------------------------------
        // Basic getters / setters
        // ---------------------------------------------------------------------
        public string GetTitle() => Title;
        public string GetSource() => Source;
        public void SetTitle(string title) => Title = title;
        public void SetSource(string src) => Source = src;
        public Dictionary<string, string> GetElements() => Elements;

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// Add (or overwrite) all key/value pairs from <paramref name="element"/>.
        /// </summary>
        public void AddElement(Dictionary<string, string> element)
        {
            foreach (var kvp in element)
                Elements[kvp.Key] = kvp.Value;
        }

        /// <summary>
        /// Loads the JSON language file pointed by <see cref="Source"/>.  
        /// Returns empty string on success, or an error message.
        /// </summary>
        public string LoadLangage()
        {
            try
            {
                string jsonContent = File.ReadAllText(Source);
                var messages = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent);

                if (messages is not null)
                    AddElement(messages);

                return "";            // success
            }
            catch (Exception e)
            {
                return e.Message;     // forward error to caller
            }
        }
    }
}
