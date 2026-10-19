using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using KrainaSurvivalu.Launcher.App.Infrastructure;
using KrainaSurvivalu.Launcher.App.Services;
using KrainaSurvivalu.Launcher.Core;

namespace KrainaSurvivalu.Launcher.App.ViewModels;

public sealed class NewsArticleViewModel(NewsArticle article)
{
    public NewsArticle Article { get; } = article;
    public string Title => Article.Title;
    public string Summary => Article.Summary;
    public string Body => Article.Body;
    public string DateLabel => Article.PublishedAt.ToLocalTime().ToString("d MMMM yyyy · HH:mm", CultureInfo.GetCultureInfo("pl-PL"));
}

public sealed class CommunityViewModel : ObservableObject
{
    private readonly IExternalLinkOpener _opener;
    private readonly Action<string> _notify;
    private CommunityContent? _content;
    private string? _contentPath;
    private string _section = "Rules";
    private string _rulesSearch = "";
    private string _loadMessage = "Wczytywanie informacji społeczności…";
    private RulesSection? _selectedRule;
    private NewsArticleViewModel? _selectedNews;
    private bool _hasLoadError;
    public ObservableCollection<RulesSection> VisibleRules { get; } = [];
    public ObservableCollection<NewsArticleViewModel> News { get; } = [];
    public ICommand ShowNewsCommand { get; }
    public ICommand ShowRulesCommand { get; }
    public RelayCommand OpenDiscordCommand { get; }
    public RelayCommand OpenRulesSourceCommand { get; }
    public RelayCommand OpenNewsSourceCommand { get; }
    public RelayCommand OpenArticleSourceCommand { get; }
    public RelayCommand PreviousRuleCommand { get; }
    public RelayCommand NextRuleCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand ReadArticleCommand { get; }
    public ICommand CloseArticleCommand { get; }
    public ICommand RefreshCommand { get; }
    public bool IsNewsSection => Section == "News";
    public bool IsRulesSection => Section == "Rules";
    public bool HasDiscord => _content?.DiscordUrl is not null;
    public string DiscordAddress => _content?.DiscordUrl ?? "Nie podano zaproszenia Discord";
    public string RulesTitle => _content?.Rules?.Title ?? "Regulamin Krainy Survivalu";
    public int RuleCount => _content?.Rules?.Sections.Count ?? 0;
    public bool HasRules => RuleCount > 0;
    public bool IsRulesMissing => !HasRules;
    public bool IsSearchEmpty => HasRules && VisibleRules.Count == 0;
    public bool HasRulesSource => _content?.RulesUrl is not null;
    public bool HasNewsSource => _content?.NewsUrl is not null;
    public bool IsNewsEmpty => News.Count == 0;
    public bool HasNews => News.Count > 0;
    public bool IsNewsList => SelectedNews is null;
    public bool IsNewsArticle => SelectedNews is not null;
    public bool HasArticleSource => SelectedNews?.Article.SourceUrl is not null;
    public string RulesCountLabel => $"{RuleCount} rozdziałów";
    public string LoadMessage { get => _loadMessage; private set => Set(ref _loadMessage, value); }
    public bool HasLoadError { get => _hasLoadError; private set => Set(ref _hasLoadError, value); }
    public string Section
    {
        get => _section;
        set
        {
            if (value is not ("News" or "Rules") || !Set(ref _section, value)) return;
            OnPropertyChanged(nameof(IsNewsSection));
            OnPropertyChanged(nameof(IsRulesSection));
        }
    }
    public string RulesSearch
    {
        get => _rulesSearch;
        set { if (Set(ref _rulesSearch, value)) FilterRules(); }
    }
    public RulesSection? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (!Set(ref _selectedRule, value)) return;
            PreviousRuleCommand.Refresh();
            NextRuleCommand.Refresh();
        }
    }
    public NewsArticleViewModel? SelectedNews
    {
        get => _selectedNews;
        set
        {
            if (!Set(ref _selectedNews, value)) return;
            OnPropertyChanged(nameof(IsNewsArticle));
            OnPropertyChanged(nameof(IsNewsList));
            OnPropertyChanged(nameof(HasArticleSource));
            OpenArticleSourceCommand.Refresh();
        }
    }
    public CommunityViewModel(IExternalLinkOpener opener, Action<string> notify)
    {
        _opener = opener;
        _notify = notify;
        ShowNewsCommand = new RelayCommand(_ => { Section = "News"; SelectedNews = null; });
        ShowRulesCommand = new RelayCommand(_ => Section = "Rules");
        OpenDiscordCommand = new RelayCommand(_ => Open(_content?.DiscordUrl), _ => HasDiscord);
        OpenRulesSourceCommand = new RelayCommand(_ => Open(_content?.RulesUrl), _ => HasRulesSource);
        OpenNewsSourceCommand = new RelayCommand(_ => Open(_content?.NewsUrl), _ => HasNewsSource);
        OpenArticleSourceCommand = new RelayCommand(_ => Open(SelectedNews?.Article.SourceUrl), _ => HasArticleSource);
        PreviousRuleCommand = new RelayCommand(_ => MoveRule(-1), _ => SelectedRule is not null && VisibleRules.IndexOf(SelectedRule) > 0);
        NextRuleCommand = new RelayCommand(_ => MoveRule(1), _ => SelectedRule is not null && VisibleRules.IndexOf(SelectedRule) < VisibleRules.Count - 1);
        ClearSearchCommand = new RelayCommand(_ => RulesSearch = "");
        ReadArticleCommand = new RelayCommand(value => { if (value is NewsArticleViewModel article && News.Contains(article)) SelectedNews = article; });
        CloseArticleCommand = new RelayCommand(_ => SelectedNews = null);
        RefreshCommand = new AsyncCommand(() => ReloadAsync(), error => _notify($"Nie udało się odświeżyć informacji: {error.Message}"));
    }
    public async Task InitializeAsync(string path)
    {
        _contentPath = Path.GetFullPath(path);
        await ReloadAsync(notify: false);
    }
    public async Task ReloadAsync(bool notify = true)
    {
        try
        {
            if (_contentPath is null) throw new InvalidOperationException("Nie wskazano pliku informacji społeczności.");
            var loaded = await CommunityCatalog.LoadAsync(_contentPath);
            Apply(loaded);
            HasLoadError = false;
            LoadMessage = "Regulamin i Discord wczytane z pliku launchera.";
            if (notify) _notify("Odświeżono regulamin i Discord.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            HasLoadError = true;
            LoadMessage = _content is null
                ? "Informacje społeczności są niedostępne. " + error.Message
                : "Nie udało się odświeżyć informacji. Zachowano ostatnią poprawną treść. " + error.Message;
            if (notify) _notify(LoadMessage);
        }
    }
    private void Apply(CommunityContent content)
    {
        _content = content;
        SelectedNews = null;
        News.Clear();
        foreach (var article in content.News) News.Add(new NewsArticleViewModel(article));
        FilterRules();
        foreach (var property in new[] { nameof(HasDiscord), nameof(DiscordAddress), nameof(RulesTitle), nameof(RuleCount), nameof(HasRules), nameof(IsRulesMissing), nameof(RulesCountLabel), nameof(HasRulesSource), nameof(HasNewsSource), nameof(IsNewsEmpty), nameof(HasNews) })
            OnPropertyChanged(property);
        foreach (var command in new[] { OpenDiscordCommand, OpenRulesSourceCommand, OpenNewsSourceCommand }) command.Refresh();
    }
    private void FilterRules()
    {
        var priorId = SelectedRule?.Id;
        var query = RulesSearch.Trim();
        VisibleRules.Clear();
        if (_content?.Rules is { } rules)
            foreach (var section in rules.Sections)
                if (query.Length == 0 || section.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) || section.Body.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    VisibleRules.Add(section);
        SelectedRule = VisibleRules.FirstOrDefault(section => section.Id == priorId) ?? VisibleRules.FirstOrDefault();
        PreviousRuleCommand.Refresh();
        NextRuleCommand.Refresh();
        OnPropertyChanged(nameof(IsSearchEmpty));
    }
    private void MoveRule(int offset)
    {
        if (SelectedRule is null) return;
        var index = VisibleRules.IndexOf(SelectedRule) + offset;
        if (index >= 0 && index < VisibleRules.Count) SelectedRule = VisibleRules[index];
    }
    private void Open(string? url)
    {
        if (url is null) return;
        try
        {
            var approved = ExternalLinkOpener.ValidateUrl(url);
            _opener.Open(approved.AbsoluteUri);
            _notify("Przekazano oficjalny link do domyślnej przeglądarki.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            _notify("Nie udało się otworzyć linku. " + error.Message);
        }
    }
}
