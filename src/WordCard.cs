using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Shiyi
{
    internal sealed class WordCard
    {
        private readonly FrameworkElement view;
        private readonly ContentControl host;
        private readonly Pronouncer voice;
        private WordEntry entry;

        public WordCard(ContentControl host, Pronouncer voice)
        {
            this.host = host;
            this.voice = voice;
            view = Ui.Load<FrameworkElement>("WordCard.xaml");
            host.Content = view;
            Find<Button>("Speak").Click += delegate {
                var current = entry;
                if (current == null) return;
                voice.Speak(current, delegate(string note) { if (entry == current) Label("VoiceNote", note); });
            };
        }

        private T Find<T>(string name) where T : FrameworkElement { return (T)view.FindName(name); }

        private void Label(string name, string value)
        {
            var label = Find<TextBlock>(name);
            label.Text = value;
            label.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }

        public void Clear()
        {
            if (entry != null) voice.Stop(entry);
            entry = null;
            host.Visibility = Visibility.Collapsed;
        }

        public void Show(WordEntry word)
        {
            Clear();
            entry = word;
            Label("Headword", word.Word);
            Label("Phonetic", word.Ipa.Length == 0 ? "" : "/" + word.Ipa + "/");
            Label("Lemma", word.Lemma.Length == 0 ? "" : "原形  " + word.Lemma);
            Label("VoiceNote", "");
            Find<Border>("ContextPanel").Visibility = word.Context == null ? Visibility.Collapsed : Visibility.Visible;
            Find<TextBlock>("NoContext").Visibility = word.Context == null ? Visibility.Visible : Visibility.Collapsed;
            var sentence = Find<TextBlock>("OriginalSentence");
            sentence.Inlines.Clear();
            if (word.Context != null)
            {
                Label("ContextMeaning", word.ContextMeaning);
                sentence.Inlines.Add(new Run(word.Context.Before));
                sentence.Inlines.Add(new Bold(new Run(word.Word)));
                sentence.Inlines.Add(new Run(word.Context.After));
            }
            Find<ItemsControl>("Senses").ItemsSource = word.Senses;
            Find<StackPanel>("PairsPanel").Visibility = word.Collocations.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            Label("Pairs", string.Join(Environment.NewLine, word.Collocations));
            Find<StackPanel>("ExamplePanel").Visibility = word.Example == null ? Visibility.Collapsed : Visibility.Visible;
            if (word.Example != null) { Label("ExampleSource", word.Example.Source); Label("ExampleTranslation", word.Example.Translation); }
            host.Visibility = Visibility.Visible;
            Find<ScrollViewer>("Details").ScrollToTop();
        }
    }
}
