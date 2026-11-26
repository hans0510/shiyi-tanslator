using System;
using System.Globalization;
using System.Linq;
using System.Speech.Synthesis;
using System.Windows.Threading;

namespace Shiyi
{
    internal sealed class Pronouncer : IDisposable
    {
        private readonly Dispatcher dispatcher;
        private SpeechSynthesizer engine;
        private WordEntry speaking;
        private Prompt active;
        private Action<string> feedback;
        private bool usedIpa;

        public Pronouncer(Dispatcher dispatcher) { this.dispatcher = dispatcher; }

        private SpeechSynthesizer Engine
        {
            get
            {
                if (engine == null)
                {
                    engine = new SpeechSynthesizer();
                    engine.SpeakCompleted += Completed;
                }
                return engine;
            }
        }

        public void Speak(WordEntry entry, Action<string> report)
        {
            if (speaking != null) Stop(speaking);
            try
            {
                if (string.IsNullOrEmpty(entry.Language)) { report("缺少源语言信息，无法选择朗读语音。"); return; }
                var culture = CultureInfo.GetCultureInfo(entry.Language);
                var voices = Engine.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo).ToArray();
                var voice = voices.FirstOrDefault(v => v.Culture.Name == culture.Name) ??
                    voices.FirstOrDefault(v => v.Culture.TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName);
                if (voice == null) { report("未安装" + culture.DisplayName + "语音，可在 Windows 语音设置中添加。"); return; }
                Engine.SelectVoice(voice.Name);
                speaking = entry;
                feedback = report;
                usedIpa = false;
                if (entry.Ipa.Length > 0 && entry.Ipa.Length <= 120)
                {
                    try
                    {
                        var prompt = new PromptBuilder(voice.Culture);
                        prompt.AppendTextWithPronunciation(entry.Word, entry.Ipa);
                        active = Engine.SpeakAsync(prompt);
                        usedIpa = true;
                    }
                    catch (ArgumentException) { /* Some installed voices accept spelling only. */ }
                    catch (FormatException) { }
                }
                if (!usedIpa) active = Engine.SpeakAsync(entry.Word);
                report("正在发音…");
            }
            catch (Exception) { speaking = null; active = null; feedback = null; report("无法播放发音，请检查 Windows 语音和音频输出。"); }
        }

        private void Completed(object sender, SpeakCompletedEventArgs e)
        {
            if (dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(delegate {
                if (e.Prompt != active || speaking == null) return;
                if (e.Error != null && usedIpa && !e.Cancelled)
                {
                    usedIpa = false;
                    try { active = Engine.SpeakAsync(speaking.Word); return; } catch (Exception) { }
                }
                var report = feedback;
                speaking = null; active = null; feedback = null;
                if (report != null) report(e.Error != null && !e.Cancelled ? "无法播放发音，请检查音频输出。" : "");
            }));
        }

        public void Stop(WordEntry entry)
        {
            if (speaking != entry || speaking == null) return;
            var previous = feedback;
            speaking = null; active = null; feedback = null;
            if (engine != null) engine.SpeakAsyncCancelAll();
            if (previous != null) previous("");
        }

        public void Dispose()
        {
            if (engine == null) return;
            speaking = null; active = null; feedback = null;
            engine.SpeakCompleted -= Completed;
            engine.Dispose();
        }
    }
}
