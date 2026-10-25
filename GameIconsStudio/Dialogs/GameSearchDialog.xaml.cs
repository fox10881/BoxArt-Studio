using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using GameIconsStudio.Services;

namespace GameIconsStudio.Dialogs;

/// <summary>
/// "Mark as wrong game" correction dialog: autocomplete-searches
/// SteamGridDB and lets the user pick the right game title.
/// </summary>
public sealed partial class GameSearchDialog : ContentDialog
{
    private readonly SteamGridService _sgdb;
    private readonly DispatcherTimer _debounce;
    private CancellationTokenSource? _searchCts;

    public ObservableCollection<GameCandidate> Results { get; } = new();

    public GameCandidate? Selected { get; private set; }
    public bool HasSelection => Selected is not null;

    /// <summary>Localized strings — read once when the dialog loads.</summary>
    public Localizer Loc => LocalizationService.Strings;

    public GameSearchDialog(SteamGridService sgdb, string initialQuery)
    {
        this.InitializeComponent();
        _sgdb = sgdb;
        QueryBox.Text = initialQuery;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await SearchAsync(QueryBox.Text.Trim());
        };
        _debounce.Start(); // initial search with the folder/shortcut name
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task SearchAsync(string query)
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();

        Results.Clear();
        Selected = null;
        Bindings.Update(); // HasSelection -> disable primary

        if (query.Length < 2)
        {
            StatusText.Text = "Type at least 2 characters";
            return;
        }

        StatusText.Text = "Searching…";
        try
        {
            var cands = await _sgdb.SearchGamesAsync(query, 10, cts.Token);
            if (cts.IsCancellationRequested) return;

            foreach (var c in cands) Results.Add(c);
            EmptyStatus.Visibility = cands.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = "No results — try a different name";
            _ = PrefetchThumbsAsync(cands, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText.Text = $"Search failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Lazily loads one cover thumb per result (vertical grid preferred),
    /// throttled so the list renders instantly and thumbs fade in as they
    /// arrive. Tied to the search token — a new query cancels stale fetches.
    /// </summary>
    private async Task PrefetchThumbsAsync(IReadOnlyList<GameCandidate> cands, CancellationToken ct)
    {
        var sem = new SemaphoreSlim(3);
        var tasks = cands.Select(async c =>
        {
            try
            {
                await sem.WaitAsync(ct);
                try
                {
                    foreach (var kind in new[] { ArtworkKind.VerticalGrid, ArtworkKind.Square, ArtworkKind.Grid })
                    {
                        var art = await _sgdb.GetArtworkCandidatesAsync(c.GameId, kind, 1, ct);
                        var thumb = art.FirstOrDefault()?.Thumb;
                        if (thumb is not null)
                        {
                            DispatcherQueue.TryEnqueue(() => c.ThumbUrl = thumb);
                            break;
                        }
                    }
                }
                finally { sem.Release(); }
            }
            catch (OperationCanceledException) { }
            catch { /* thumb stays on placeholder */ }
        });
        await Task.WhenAll(tasks);
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Selected = ResultsList.SelectedItem as GameCandidate;
        Bindings.Update();
    }

    private void ResultsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GameCandidate c)
        {
            ResultsList.SelectedItem = c;
        }
    }
}
