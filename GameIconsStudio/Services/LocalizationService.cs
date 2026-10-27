using System;
using System.Collections.Generic;
using Windows.Globalization;

namespace GameIconsStudio.Services;

/// <summary>
/// Runtime string table for en-US / ar-SA. The canonical translations also
/// live in Strings/en-US|ar-SA/Resources.resw; this service exists because
/// live language switching (no restart) requires bound string properties
/// rather than static x:Uid resource lookup.
/// </summary>
public static class LocalizationService
{
    public const string English = "en-US";
    public const string Arabic = "ar-SA";
    private const string SettingKey = "AppLanguage";

    private static readonly Dictionary<string, string> En = new()
    {
        ["AppTitle"] = "BoxArt Studio",
        ["HeroBadge"] = "PC box art · 2:3 covers",
        ["HeroTitleA"] = "Modernize your ",
        ["HeroTitleB"] = "Games folder.",
        ["HeroSub"] = "Turn plain shortcuts and folders into a beautiful library of vertical box art covers. Pick a mode below and let BoxArt Studio do the rest.",
        ["ChooseMode"] = "CHOOSE A MODE",
        ["ModeShortcuts"] = "Shortcuts Folder",
        ["ModeShortcutsDesc"] = "Scan a directory of .lnk files and match each game to its cover.",
        ["ModeSubFolders"] = "All Sub-Folders",
        ["ModeSubFoldersDesc"] = "Bulk customize every game folder inside a parent directory.",
        ["ModeFolder"] = "Specific Folder",
        ["ModeFolderDesc"] = "Target a single game folder and give it a custom cover.",
        ["ModePicks"] = "Specific Shortcuts",
        ["ModePicksDesc"] = "Pick individual game files and style them one by one.",
        ["RepairTitle"] = "Repair Icon Cache",
        ["RepairDesc"] = "Rebuild the Windows icon cache so new covers show up instantly.",
        ["SystemTool"] = "SYSTEM TOOL",
        ["RunLabel"] = "Run",
        ["BackHome"] = "Back to Home",
        ["ModeLabel"] = "Mode:",
        ["SettingsTooltip"] = "SteamGridDB API settings",
        ["ScannedLibrary"] = "SCANNED LIBRARY",
        ["SearchPlaceholder"] = "Search games",
        ["CoverPreview"] = "COVER PREVIEW · 2:3",
        ["CaseBadges"] = "CASE BADGES",
        ["ShortcutLabel"] = "Shortcut",
        ["CoverSourceLabel"] = "Cover source",
        ["MatchConfidence"] = "Match confidence",
        ["PlatformHeading"] = "PLATFORM TEMPLATE",
        ["PickTemplate"] = "Pick a case template for this game",
        ["BrowseTemplateTip"] = "Import a custom transparent PNG/WebP template",
        ["SurfaceHeading"] = "SURFACE FINISH",
        ["PickSurface"] = "Pick a finish for this cover",
        ["OverlayOriginal"] = "Original",
        ["PlatformNone"] = "None",
        ["SurfaceNone"] = "Natural",
        ["SurfaceGlossy"] = "Glossy",
        ["SurfaceMatte"] = "Matte",
        ["SurfaceHolo"] = "Holographic",
        ["SurfaceVintage"] = "Worn",
        ["StretchSquareLabel"] = "Stretch to Square (1:1)",
        ["BtnChooseCover"] = "Choose different cover",
        ["BtnWrongGame"] = "Mark as wrong game",
        ["BtnRemoveIcon"] = "Remove Custom Icon",
        ["ToastIconRemoved"] = "Custom icon removed",
        ["ToastIconRemovedBody"] = "Reverted the icon for {0}.",
        ["BtnGoogle"] = "Open Google Images",
        ["BtnAiGenerate"] = "Generate with AI",
        ["AiDialogTitle"] = "Generate AI Cover",
        ["AiSubtitle"] = "Copy the original icon, then paste it into Bing Image Creator to generate a professional cover.",
        ["AiNameLabel"] = "Program Name",
        ["AiDescLabel"] = "Program Function / Description",
        ["AiDescPlaceholder"] = "e.g., Photo editing, 3D design, etc.",
        ["AiCopyAssets"] = "Copy Assets (Paste the program icon to improve output quality)",
        ["AiBtnCopyIcon"] = "Copy Original Icon",
        ["AiBtnCopyPrompt"] = "Copy AI Prompt",
        ["AiBtnGo"] = "Go",
        ["AiBtnCancel"] = "Cancel",
        ["AiCopiedIcon"] = "Icon copied to clipboard ✓",
        ["AiCopiedPrompt"] = "AI prompt copied to clipboard ✓",
        ["AiCopyError"] = "Couldn't copy icon: ",
        ["BtnPaste"] = "Paste",
        ["BtnBrowse"] = "Browse PC",
        ["AltCovers"] = "ALTERNATIVE COVERS",
        ["AltCoversSuffix"] = " from SteamGridDB · click to apply",
        ["AltCoversEmpty"] = "No alternative covers yet — they load automatically after a match, or press 'Choose different cover'.",
        ["Live"] = "LIVE",
        ["ProcessedLabel"] = "Processed",
        ["MatchedLabel"] = "Matched",
        ["AttentionLabel"] = "Attention",
        ["SuccessRateLabel"] = "Success rate",
        ["BtnBuild"] = "Apply & Build Icons",
        ["StatusMatched"] = "Matched",
        ["StatusAttention"] = "Needs attention",
        ["GamesSuffix"] = " games",
        ["DialogTitle"] = "Find the correct game",
        ["DialogPrimary"] = "Use selected game",
        ["Cancel"] = "Cancel",
        ["SearchDialogPlaceholder"] = "Search SteamGridDB",
        ["SearchDialogHint"] = "Type to search SteamGridDB",
        ["SearchDialogFooter"] = "Pick the correct game — its covers will replace the current match.",
        ["RepairDialogTitle"] = "Repair Icon Cache",
        ["RepairDialogBody"] = "Rebuild the Windows icon cache so new covers show up instantly.\n\nThis broadcasts a shell icon refresh (SHChangeNotify + ie4uinit). Explorer is not restarted.",
        ["RepairDialogPrimary"] = "Repair",
        ["LangButton"] = "عربي",
        ["LangToastTitle"] = "Interface language",
        ["LangToastBody"] = "Switched to English.",
        ["DebugTooltip"] = "Debug Logs",
        ["DebugTitle"] = "Debug Logs",
        ["BtnCopyLogs"] = "Copy Logs",
        ["BtnClearLogs"] = "Clear",
        ["BtnClose"] = "Close",
        ["DropZone"] = "Drag and Drop",
        ["DropZoneDesc"] = "Drop any shortcut here to edit it immediately.",
    };

    private static readonly Dictionary<string, string> Ar = new()
    {
        ["AppTitle"] = "BoxArt Studio",
        ["HeroBadge"] = "غلاف كرتوني PC · أغلفة 2:3",
        ["HeroTitleA"] = "حدّث شكل ",
        ["HeroTitleB"] = "ألعابك.",
        ["HeroSub"] = "حوّل الاختصارات والمجلدات العادية إلى مكتبة ألعاب جميلة ومليئة بالحياة.",
        ["ChooseMode"] = "اختر الوضع",
        ["ModeShortcuts"] = "مجلد اختصارات",
        ["ModeShortcutsDesc"] = "افحص مجلدًا يحتوي على ملفات .lnk وطابق كل لعبة مع غلافها.",
        ["ModeSubFolders"] = "جميع المجلدات الفرعية",
        ["ModeSubFoldersDesc"] = "خصّص كل مجلدات الألعاب داخل مجلد رئيسي دفعة واحدة.",
        ["ModeFolder"] = "مجلد محدد",
        ["ModeFolderDesc"] = "استهدف مجلد لعبة واحدًا وامنحه غلافًا مخصصًا.",
        ["ModePicks"] = "اختصارات محددة",
        ["ModePicksDesc"] = "اختر ملفات ألعاب فردية وصمّمها واحدًا واحدًا.",
        ["RepairTitle"] = "إصلاح أيقونات النظام",
        ["RepairDesc"] = "أعد بناء ذاكرة أيقونات Windows لكي تظهر الأغلفة الجديدة فورًا.",
        ["SystemTool"] = "أداة نظام",
        ["RunLabel"] = "تشغيل",
        ["BackHome"] = "العودة إلى الرئيسية",
        ["ModeLabel"] = "الوضع:",
        ["SettingsTooltip"] = "إعدادات SteamGridDB API",
        ["ScannedLibrary"] = "المكتبة المفحوصة",
        ["SearchPlaceholder"] = "ابحث عن الألعاب",
        ["CoverPreview"] = "معاينة الغلاف · 2:3",
        ["CaseBadges"] = "شارات الغلاف",
        ["ShortcutLabel"] = "الاختصار",
        ["CoverSourceLabel"] = "مصدر الغلاف",
        ["MatchConfidence"] = "نسبة التطابق",
        ["PlatformHeading"] = "قالب المنصة",
        ["PickTemplate"] = "اختر قالب العلبة لهذه اللعبة",
        ["BrowseTemplateTip"] = "استورد قالبًا مخصصًا شفافًا (PNG/WebP)",
        ["SurfaceHeading"] = "تشطيبة السطح",
        ["PickSurface"] = "اختر تشطيبة لسطح الغلاف",
        ["OverlayOriginal"] = "الأصلي",
        ["PlatformNone"] = "بدون",
        ["SurfaceNone"] = "طبيعي",
        ["SurfaceGlossy"] = "لامع",
        ["SurfaceMatte"] = "مطفي",
        ["SurfaceHolo"] = "هولوجرام",
        ["SurfaceVintage"] = "قديم",
        ["StretchSquareLabel"] = "تمطيط لمربع 1:1",
        ["BtnChooseCover"] = "اختيار غلاف مختلف",
        ["BtnWrongGame"] = "تحديد كلعبة خاطئة",
        ["BtnRemoveIcon"] = "إلغاء التخصيص",
        ["ToastIconRemoved"] = "تم إلغاء التخصيص",
        ["ToastIconRemovedBody"] = "تمت إعادة أيقونة {0}.",
        ["BtnGoogle"] = "صور جوجل",
        ["BtnAiGenerate"] = "توليد بالذكاء الاصطناعي",
        ["AiDialogTitle"] = "توليد غلاف بالذكاء الاصطناعي",
        ["AiSubtitle"] = "انسخ الأيقونة القديمة، ثم الصقها في Bing Image Creator لتوليد غلاف احترافي.",
        ["AiNameLabel"] = "اسم البرنامج",
        ["AiDescLabel"] = "وصف الفائدة من البرنامج",
        ["AiDescPlaceholder"] = "مثال: تعديل الصور، تصميم هندسي، الخ...",
        ["AiCopyAssets"] = "نسخ العناصر (الصق أيقونة البرنامج لتحسين جودة الصورة المخرجة)",
        ["AiBtnCopyIcon"] = "نسخ الأيقونة القديمة",
        ["AiBtnCopyPrompt"] = "نسخ وصف الذكاء الاصطناعي",
        ["AiBtnGo"] = "ذهاب",
        ["AiBtnCancel"] = "إلغاء",
        ["AiCopiedIcon"] = "تم نسخ الأيقونة إلى الحافظة ✓",
        ["AiCopiedPrompt"] = "تم نسخ وصف الذكاء الاصطناعي إلى الحافظة ✓",
        ["AiCopyError"] = "تعذّر نسخ الأيقونة: ",
        ["BtnPaste"] = "لصق",
        ["BtnBrowse"] = "تصفح الجهاز",
        ["AltCovers"] = "أغلفة بديلة",
        ["AltCoversSuffix"] = " من SteamGridDB · انقر للتطبيق",
        ["AltCoversEmpty"] = "لا توجد أغلفة بديلة بعد — يتم تحميلها تلقائيًا بعد المطابقة، أو اضغط «اختيار غلاف مختلف».",
        ["Live"] = "مباشر",
        ["ProcessedLabel"] = "المعالجة",
        ["MatchedLabel"] = "المطابقة",
        ["AttentionLabel"] = "تحتاج مراجعة",
        ["SuccessRateLabel"] = "نسبة النجاح",
        ["BtnBuild"] = "تطبيق وبناء الأيقونات",
        ["StatusMatched"] = "تمت المطابقة",
        ["StatusAttention"] = "تحتاج مراجعة",
        ["GamesSuffix"] = " لعبة",
        ["DialogTitle"] = "ابحث عن اللعبة الصحيحة",
        ["DialogPrimary"] = "استخدام اللعبة المحددة",
        ["Cancel"] = "إلغاء",
        ["SearchDialogPlaceholder"] = "ابحث في SteamGridDB",
        ["SearchDialogHint"] = "اكتب للبحث في SteamGridDB",
        ["SearchDialogFooter"] = "اختر اللعبة الصحيحة — ستحل أغلفتها محل المطابقة الحالية.",
        ["RepairDialogTitle"] = "إصلاح أيقونات النظام",
        ["RepairDialogBody"] = "أعد بناء ذاكرة أيقونات Windows لكي تظهر الأغلفة الجديدة فورًا.\n\nيبث ذلك تحديث أيقونات النظام (SHChangeNotify + ie4uinit). لن تتم إعادة تشغيل Explorer.",
        ["RepairDialogPrimary"] = "إصلاح",
        ["LangButton"] = "English",
        ["LangToastTitle"] = "لغة الواجهة",
        ["LangToastBody"] = "تم التبديل إلى العربية.",
        ["DebugTooltip"] = "سجل الأخطاء",
        ["DebugTitle"] = "سجل الأخطاء",
        ["BtnCopyLogs"] = "نسخ السجل",
        ["BtnClearLogs"] = "مسح",
        ["BtnClose"] = "إغلاق",
        ["DropZone"] = "سحب وإفلات",
        ["DropZoneDesc"] = "أفلت أي اختصار هنا لتعديله فوراً.",
    };

    public static string Current { get; private set; } = English;
    public static bool IsRtl => Current == Arabic;

    /// <summary>Raised on the UI thread after <see cref="SetLanguage"/>.</summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>Live-bound string table — re-evaluated when Loc changes.</summary>
    public static Localizer Strings { get; } = new();

    public static string T(string key) =>
        (IsRtl ? Ar : En).TryGetValue(key, out var v) ? v : En.GetValueOrDefault(key, key);

    /// <summary>Load the persisted language at startup.</summary>
    public static void Init()
    {
        var saved = SettingsService.GetSetting(SettingKey);
        Current = saved == Arabic ? Arabic : English;
        try { ApplicationLanguages.PrimaryLanguageOverride = Current; } catch { }
    }

    /// <summary>Switch language live: persists choice, sets MRT context, fires event.</summary>
    public static void SetLanguage(string lang)
    {
        Current = lang == Arabic ? Arabic : English;
        SettingsService.SetSetting(SettingKey, Current);
        try { ApplicationLanguages.PrimaryLanguageOverride = Current; } catch { }
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }
}

/// <summary>
/// Bindable facade: every property reads the current language's string.
/// Views bind <c>VM.Loc.Key</c>; the VM raises PropertyChanged("Loc")
/// when the language changes, so all labels refresh live.
/// </summary>
public sealed class Localizer
{
    public string AppTitle => LocalizationService.T(nameof(AppTitle));
    public string HeroBadge => LocalizationService.T(nameof(HeroBadge));
    public string HeroTitleA => LocalizationService.T(nameof(HeroTitleA));
    public string HeroTitleB => LocalizationService.T(nameof(HeroTitleB));
    public string HeroSub => LocalizationService.T(nameof(HeroSub));
    public string ChooseMode => LocalizationService.T(nameof(ChooseMode));
    public string ModeShortcuts => LocalizationService.T(nameof(ModeShortcuts));
    public string ModeShortcutsDesc => LocalizationService.T(nameof(ModeShortcutsDesc));
    public string ModeSubFolders => LocalizationService.T(nameof(ModeSubFolders));
    public string ModeSubFoldersDesc => LocalizationService.T(nameof(ModeSubFoldersDesc));
    public string ModeFolder => LocalizationService.T(nameof(ModeFolder));
    public string ModeFolderDesc => LocalizationService.T(nameof(ModeFolderDesc));
    public string ModePicks => LocalizationService.T(nameof(ModePicks));
    public string ModePicksDesc => LocalizationService.T(nameof(ModePicksDesc));
    public string RepairTitle => LocalizationService.T(nameof(RepairTitle));
    public string RepairDesc => LocalizationService.T(nameof(RepairDesc));
    public string SystemTool => LocalizationService.T(nameof(SystemTool));
    public string RunLabel => LocalizationService.T(nameof(RunLabel));
    public string BackHome => LocalizationService.T(nameof(BackHome));
    public string ModeLabel => LocalizationService.T(nameof(ModeLabel));
    public string SettingsTooltip => LocalizationService.T(nameof(SettingsTooltip));
    public string ScannedLibrary => LocalizationService.T(nameof(ScannedLibrary));
    public string SearchPlaceholder => LocalizationService.T(nameof(SearchPlaceholder));
    public string CoverPreview => LocalizationService.T(nameof(CoverPreview));
    public string CaseBadges => LocalizationService.T(nameof(CaseBadges));
    public string ShortcutLabel => LocalizationService.T(nameof(ShortcutLabel));
    public string CoverSourceLabel => LocalizationService.T(nameof(CoverSourceLabel));
    public string MatchConfidence => LocalizationService.T(nameof(MatchConfidence));
    public string PlatformHeading => LocalizationService.T(nameof(PlatformHeading));
    public string PickTemplate => LocalizationService.T(nameof(PickTemplate));
    public string BrowseTemplateTip => LocalizationService.T(nameof(BrowseTemplateTip));
    public string SurfaceHeading => LocalizationService.T(nameof(SurfaceHeading));
    public string PickSurface => LocalizationService.T(nameof(PickSurface));
    public string OverlayOriginal => LocalizationService.T(nameof(OverlayOriginal));
    public string PlatformNone => LocalizationService.T(nameof(PlatformNone));
    public string SurfaceNone => LocalizationService.T(nameof(SurfaceNone));
    public string SurfaceGlossy => LocalizationService.T(nameof(SurfaceGlossy));
    public string SurfaceMatte => LocalizationService.T(nameof(SurfaceMatte));
    public string SurfaceHolo => LocalizationService.T(nameof(SurfaceHolo));
    public string SurfaceVintage => LocalizationService.T(nameof(SurfaceVintage));
    public string StretchSquareLabel => LocalizationService.T(nameof(StretchSquareLabel));
    public string BtnChooseCover => LocalizationService.T(nameof(BtnChooseCover));
    public string BtnWrongGame => LocalizationService.T(nameof(BtnWrongGame));
    public string BtnRemoveIcon => LocalizationService.T(nameof(BtnRemoveIcon));
    public string BtnGoogle => LocalizationService.T(nameof(BtnGoogle));
    public string BtnAiGenerate => LocalizationService.T(nameof(BtnAiGenerate));
    public string AiDialogTitle => LocalizationService.T(nameof(AiDialogTitle));
    public string AiSubtitle => LocalizationService.T(nameof(AiSubtitle));
    public string AiNameLabel => LocalizationService.T(nameof(AiNameLabel));
    public string AiDescLabel => LocalizationService.T(nameof(AiDescLabel));
    public string AiDescPlaceholder => LocalizationService.T(nameof(AiDescPlaceholder));
    public string AiCopyAssets => LocalizationService.T(nameof(AiCopyAssets));
    public string AiBtnCopyIcon => LocalizationService.T(nameof(AiBtnCopyIcon));
    public string AiBtnCopyPrompt => LocalizationService.T(nameof(AiBtnCopyPrompt));
    public string AiBtnGo => LocalizationService.T(nameof(AiBtnGo));
    public string AiBtnCancel => LocalizationService.T(nameof(AiBtnCancel));
    public string AiCopiedIcon => LocalizationService.T(nameof(AiCopiedIcon));
    public string AiCopiedPrompt => LocalizationService.T(nameof(AiCopiedPrompt));
    public string AiCopyError => LocalizationService.T(nameof(AiCopyError));
    public string BtnPaste => LocalizationService.T(nameof(BtnPaste));
    public string BtnBrowse => LocalizationService.T(nameof(BtnBrowse));
    public string AltCovers => LocalizationService.T(nameof(AltCovers));
    public string AltCoversSuffix => LocalizationService.T(nameof(AltCoversSuffix));
    public string AltCoversEmpty => LocalizationService.T(nameof(AltCoversEmpty));
    public string Live => LocalizationService.T(nameof(Live));
    public string ProcessedLabel => LocalizationService.T(nameof(ProcessedLabel));
    public string MatchedLabel => LocalizationService.T(nameof(MatchedLabel));
    public string AttentionLabel => LocalizationService.T(nameof(AttentionLabel));
    public string SuccessRateLabel => LocalizationService.T(nameof(SuccessRateLabel));
    public string BtnBuild => LocalizationService.T(nameof(BtnBuild));
    public string DialogTitle => LocalizationService.T(nameof(DialogTitle));
    public string DialogPrimary => LocalizationService.T(nameof(DialogPrimary));
    public string Cancel => LocalizationService.T(nameof(Cancel));
    public string SearchDialogPlaceholder => LocalizationService.T(nameof(SearchDialogPlaceholder));
    public string SearchDialogHint => LocalizationService.T(nameof(SearchDialogHint));
    public string SearchDialogFooter => LocalizationService.T(nameof(SearchDialogFooter));
    public string LangButton => LocalizationService.T(nameof(LangButton));
    public string DebugTooltip => LocalizationService.T(nameof(DebugTooltip));
    public string DebugTitle => LocalizationService.T(nameof(DebugTitle));
    public string BtnCopyLogs => LocalizationService.T(nameof(BtnCopyLogs));
    public string BtnClearLogs => LocalizationService.T(nameof(BtnClearLogs));
    public string BtnClose => LocalizationService.T(nameof(BtnClose));
    public string DropZone => LocalizationService.T(nameof(DropZone));
    public string DropZoneDesc => LocalizationService.T(nameof(DropZoneDesc));
}
