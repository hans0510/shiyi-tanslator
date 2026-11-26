using System;
using System.Web.Script.Serialization;

namespace Shiyi
{
    internal static class DictionaryTests
    {
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
        }

        public static void Main()
        {
            foreach (string word in new[] { "bank", "don't", "well-being", "café", "U.S.", "Dr.", "COVID-19", "银行" })
                Check(new LookupQuery(word).Word == word, "Expected word: " + word);
            foreach (string text in new[] { "Go!", "We sat on the bank.", "你好！", "a river bank", "one\ntwo", "3.14" })
                Check(new LookupQuery(text).Word == null, "Expected direct translation: " + text);
            Check(new LookupQuery("“bank,”").Word == "bank", "Quoted selection");
            Check(new LookupQuery("明天去学校").MayBeSentence, "Unsegmented clause");

            var lastBank = SentenceContext.Extract("Other sentence. The bank stood by the river ", "bank", ". Next sentence.", "bank");
            Check(lastBank.Before == "The bank stood by the river " && lastBank.After == ".", "Exact occurrence only");
            var chinese = SentenceContext.Extract("上一句。他在", "银行", "工作。下一句。", "银行");
            Check(chinese.Before == "他在" && chinese.After == "工作。", "CJK boundaries");
            var decimalContext = SentenceContext.Extract("Dr. Li paid 3.14 ", "dollars,", " then left.", "dollars");
            Check(decimalContext.Before == "Dr. Li paid 3.14 " && decimalContext.After == ", then left.", "Abbreviations, decimal and punctuation");
            var quoted = SentenceContext.Extract("She said “Done.” A U.S. ", "bank", " opened today.", "bank");
            Check(quoted.Before == "A U.S. " && quoted.After == " opened today.", "Closing quote and initialism");
            Check(SentenceContext.Extract("Previous.\n", "word", "\r\nNext.", "word") == null, "Do not borrow the next paragraph");
            Check(SentenceContext.Extract(new string('a', SentenceContext.SideLimit + 1), "word", "", "word") == null, "Reject unbounded provider ranges");

            var json = new JavaScriptSerializer();
            string card = "{\"kind\":\"word\",\"language\":\"en-US\",\"ipa\":\"/bæŋk/\",\"contextMeaning\":\"河岸\",\"senses\":[{\"pos\":\"n.\",\"meaning\":\"银行\"}]}";
            var query = new LookupQuery("bank");
            var result = LookupResult.Parse("~~~json\n" + card + "\n~~~", query, lastBank, json);
            Check(result.Entry.Ipa == "bæŋk" && result.Entry.Senses[0].Pos == "n.", "Dictionary fields");
            Check(result.Text.Contains("The bank stood by the river bank.") && result.Text.Contains("河岸"), "Copy keeps original and contextual sense");
            var standalone = LookupResult.Parse(card, query, null, json);
            Check(standalone.Entry.ContextMeaning == "" && !standalone.Text.Contains("句中义"), "Ignore any model-invented context");
            var sentence = LookupResult.Parse("{\"kind\":\"translation\",\"translation\":\"I'll go tomorrow.\"}", new LookupQuery("我明天去"), null, json);
            Check(sentence.Entry == null && sentence.Text == "I'll go tomorrow.", "Unsegmented sentence is plain translation");
            foreach (string malformed in new[] { "not JSON", "{\"kind\":\"word\",\"senses\":[]}", "{\"kind\":\"unknown\"}" })
            {
                bool rejected = false;
                try { LookupResult.Parse(malformed, query, null, json); } catch (TranslationException) { rejected = true; }
                Check(rejected, "Malformed or unknown entry");
            }
            Console.WriteLine("Dictionary checks passed.");
            SettingsTests.Run();
        }
    }
}
