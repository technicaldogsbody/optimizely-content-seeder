using Bogus;
using Bogus.Bson;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;

/// <summary>
/// Gives locales that Bogus has no lorem data for a small word list of their own.
/// <para>
/// Without it Bogus falls back to the English lorem set for those locales, so a Chinese or Ukrainian page is filled
/// with Latin "Lorem ipsum" — wrong script, which defeats testing fonts, line breaking and text width.
/// Latin-script languages (Swedish, German…) are left on the Latin lorem set, which looks right at a glance.
/// </para>
/// Bogus keeps its locale data in a process-wide store, so this installs each list once and only for a locale that has
/// no lorem of its own; locales Bogus already covers (Japanese, Korean, Arabic, Russian…) are untouched.
/// </summary>
internal static class LocalizedLorem
{
    // Plain common vocabulary (people, places, nature, work, shopping) so sentences read as ordinary text.
    private static readonly Dictionary<string, string[]> _words = new(StringComparer.Ordinal)
    {
        ["zh_CN"] = Split("我们 你们 他们 这个 那个 一些 可以 需要 已经 因为 所以 但是 如果 然后 开始 结束 工作 生活 学习 时间 朋友 家庭 世界 国家 城市 公司 产品 服务 市场 技术 经验 问题 方法 结果 计划 项目 管理 发展 质量 安全 环境 健康 教育 文化 历史 自然 旅行 户外 装备 选择 提供 提高 支持 使用 创造 分享 喜欢 重要 简单 美丽 新鲜 快乐"),
        ["zh_TW"] = Split("我們 你們 他們 這個 那個 一些 可以 需要 已經 因為 所以 但是 如果 然後 開始 結束 工作 生活 學習 時間 朋友 家庭 世界 國家 城市 公司 產品 服務 市場 技術 經驗 問題 方法 結果 計劃 專案 管理 發展 品質 安全 環境 健康 教育 文化 歷史 自然 旅行 戶外 裝備 選擇 提供 提高 支援 使用 創造 分享 喜歡 重要 簡單 美麗 新鮮 快樂"),
        ["uk"] = Split("люди час життя робота дім місто країна світ день ніч вода земля дорога гора ліс річка небо сонце друг сім'я книга слово голос рух шлях добро краса сила думка справа місце школа вулиця хліб подорож природа якість досвід послуга ринок проєкт розвиток безпека здоров'я культура історія вибір підтримка"),
        ["el"] = Split("άνθρωπος χρόνος ζωή δουλειά σπίτι πόλη χώρα κόσμος ημέρα νύχτα νερό γη δρόμος βουνό δάσος ποτάμι ουρανός ήλιος φίλος οικογένεια βιβλίο λέξη φωνή κίνηση μονοπάτι καλοσύνη ομορφιά δύναμη σκέψη υπόθεση μέρος σχολείο ψωμί ταξίδι φύση ποιότητα εμπειρία υπηρεσία αγορά έργο ανάπτυξη ασφάλεια υγεία πολιτισμός ιστορία επιλογή υποστήριξη"),
        ["ge"] = Split("ადამიანი დრო ცხოვრება სამუშაო სახლი ქალაქი ქვეყანა სამყარო დღე ღამე წყალი მიწა გზა მთა ტყე მდინარე ცა მზე მეგობარი ოჯახი წიგნი სიტყვა ხმა"),
        ["ne"] = Split("मान्छे समय जीवन काम घर शहर देश संसार दिन रात पानी माटो बाटो पहाड जङ्गल नदी आकाश घाम साथी परिवार किताब शब्द आवाज"),
    };

    private static readonly object _gate = new();
    private static readonly HashSet<string> _done = new(StringComparer.Ordinal);

    /// <summary>The Bogus locales this provides words for.</summary>
    internal static IEnumerable<string> Locales => _words.Keys;

    /// <summary>Makes sure <paramref name="locale"/> produces lorem in its own script, if we have words for it.</summary>
    internal static void Ensure(string locale)
    {
        if (!_words.TryGetValue(locale, out var words))
            return;

        lock (_gate)
        {
            if (!_done.Add(locale))
                return;

            // Never override a locale Bogus covers itself.
            if (HasOwnLorem(locale))
                return;

            var data = Database.GetLocale(locale);
            var array = new BArray();
            foreach (string word in words)
                array.Add(new BValue(word));

            // Some locales (Greek, Spanish…) have a lorem entry that still falls back to English words.
            if (data.TryGetValue("lorem", out var existing) && existing is BObject lorem)
            {
                lorem.Remove("words");
            }
            else
            {
                lorem = new BObject();
                data.Add("lorem", lorem);
            }

            lorem.Add("words", array);
        }
    }

    // A locale has lorem of its own when it produces words that are not in the English set.
    // (HasKey is unreliable here: some locales have the key yet still fall back to English.)
    private static bool HasOwnLorem(string locale)
    {
        var english = ((BArray)Database.Get("lorem", "words", "en")).Cast<BValue>().Select(v => v.StringValue).ToHashSet(StringComparer.Ordinal);
        var faker = new Faker(locale);
        return Enumerable.Range(0, 30).Any(_ => !english.Contains(faker.Lorem.Word(), StringComparer.Ordinal));
    }

    private static string[] Split(string words) => words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
