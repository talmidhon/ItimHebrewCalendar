using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ItimHebrewCalendar.Models;
using ItimHebrewCalendar.Services;

namespace ItimHebrewCalendar.Windows
{
    public sealed partial class EventEditorWindow : Window
    {
        private static readonly string[] HebrewMonthNames =
        {
            "", "ניסן", "אייר", "סיון", "תמוז", "אב", "אלול",
            "תשרי", "חשון", "כסלו", "טבת", "שבט", "אדר", "אדר ב'"
        };

        private BackdropHandles? _backdrop;
        private UserEvent _event;
        private readonly bool _isNew;
        private List<ReminderRule> _reminders = new();
        private Action? _onSavedOrDeleted;

        public EventEditorWindow(UserEvent? existing, DateTime defaultDate, Action? onSavedOrDeleted = null)
        {
            InitializeComponent();
            _onSavedOrDeleted = onSavedOrDeleted;

            _isNew = existing == null;
            _event = existing ?? CreateNewWithDefaults(defaultDate);

            Title = _isNew ? "אירוע חדש - עיתים" : "עריכת אירוע - עיתים";
            HeaderText.Text = _isNew ? "אירוע חדש" : "עריכת אירוע";
            TitleBarText.Text = HeaderText.Text;
            DeleteButton.Visibility = _isNew ? Visibility.Collapsed : Visibility.Visible;

            RootGrid.FlowDirection = FlowDirection.RightToLeft;
            ThemeHelper.EnableRtlCaptionButtons(this);
            WindowHelpers.SetupCustomTitleBar(this, AppTitleBar);
            _backdrop = WindowHelpers.TrySetBackdrop(this);
            ThemeHelper.Apply(this, App.Settings.Theme, _backdrop.Config);
            WindowHelpers.Resize(this, 460, 760);
            WindowHelpers.PositionNearCursor(this);

            PopulateHebrewControls();
            LoadFromEvent(_event);
            RebuildRemindersUi();

            Closed += (_, _) =>
            {
                _backdrop?.Dispose();
                _backdrop = null;
            };
        }

        // ─── Populate ──────────────────────────────────────────────────────────────

        private void PopulateHebrewControls()
        {
            HebDayCombo.Items.Clear();
            for (int d = 1; d <= 30; d++)
                HebDayCombo.Items.Add(new ComboBoxItem { Content = HebrewNumberFormatter.FormatDay(d), Tag = d });

            HebMonthCombo.Items.Clear();
            for (int m = 1; m <= 13; m++)
                HebMonthCombo.Items.Add(new ComboBoxItem { Content = HebrewMonthNames[m], Tag = m });

            HebYearBox.Minimum = 5000;
            HebYearBox.Maximum = 6000;
            HebYearBox.Value = 5786;
        }

        private static UserEvent CreateNewWithDefaults(DateTime defaultDate)
        {
            var ev = new UserEvent { StartGregorian = defaultDate.Date };
            try
            {
                var hd = HebcalBridge.Convert(defaultDate.Date);
                if (hd != null)
                {
                    ev.StartHebrew = new HebrewDateRef
                    {
                        Year  = hd.HebYear,
                        Month = hd.HebMonth,
                        Day   = hd.HebDay
                    };
                }
            }
            catch (Exception ex) { SettingsManager.LogError("EventEditorWindow.NewDefaults", ex); }
            return ev;
        }

        private void LoadFromEvent(UserEvent ev)
        {
            TitleBox.Text = ev.Title ?? "";
            DescriptionBox.Text = ev.Description ?? "";

            // Default to Hebrew (index 0 after the XAML reorder)
            DateModeCombo.SelectedIndex = 0;
            if (ev.StartGregorian.HasValue)
                GregDatePicker.Date = ev.StartGregorian.Value;
            if (ev.StartHebrew != null)
            {
                SelectByTag(HebDayCombo, ev.StartHebrew.Day);
                SelectByTag(HebMonthCombo, ev.StartHebrew.Month);
                HebYearBox.Value = ev.StartHebrew.Year;
            }

            if (ev.StartTime.HasValue)
            {
                AllDaySwitch.IsOn = false;
                StartTimePicker.Time = ev.StartTime.Value;
            }
            else
            {
                AllDaySwitch.IsOn = true;
            }
            DurationMinutesBox.Value = ev.Duration?.TotalMinutes ?? double.NaN;

            ApplyAllDay(AllDaySwitch.IsOn);

            // Recurrence
            var rec = ev.Recurrence;
            string tag = (rec?.Kind ?? RecurrenceKind.None).ToString();
            SelectByTag(RecurrenceCombo, tag);
            if (rec != null)
            {
                IntervalBox.Value = rec.Interval;
                if (rec.Until.HasValue) UntilPicker.Date = rec.Until.Value;
                if (rec.Weekdays.HasValue) ApplyWeekdayMask(rec.Weekdays.Value);
            }

            _reminders = ev.Reminders.Select(CloneRule).ToList();
        }

        private static ReminderRule CloneRule(ReminderRule r) => new()
        {
            Id = r.Id,
            AnchorKind = r.AnchorKind,
            ZmanAnchors = r.ZmanAnchors.Select(a => new ZmanimAnchor { Zman = a.Zman }).ToList(),
            ZmanCombination = r.ZmanCombination,
            OffsetMinutes = r.OffsetMinutes,
            Enabled = r.Enabled
        };

        // ─── Event handlers ────────────────────────────────────────────────────────

        private void DateModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool greg = (DateModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Greg";
            GregDatePicker.Visibility = greg ? Visibility.Visible : Visibility.Collapsed;
            HebDatePanel.Visibility   = greg ? Visibility.Collapsed : Visibility.Visible;
        }

        private void AllDaySwitch_Toggled(object sender, RoutedEventArgs e) => ApplyAllDay(AllDaySwitch.IsOn);

        private void ApplyAllDay(bool allDay)
        {
            StartTimePicker.IsEnabled = !allDay;
            DurationMinutesBox.IsEnabled = !allDay;
            TimePanel.Opacity = allDay ? 0.5 : 1.0;
        }

        private void RecurrenceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tag = (RecurrenceCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            bool none = tag == "None" || string.IsNullOrEmpty(tag);
            RecurrenceDetailsPanel.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
            WeekdaysPanel.Visibility = tag == "WeeklyGregorian" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnAddReminder(object sender, RoutedEventArgs e)
        {
            _reminders.Add(new ReminderRule
            {
                AnchorKind = ReminderAnchorKind.FixedOffset,
                OffsetMinutes = -10
            });
            RebuildRemindersUi();
        }

        private void OnCancel(object sender, RoutedEventArgs e) => Close();

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            if (_isNew) { Close(); return; }
            EventsRepository.Delete(_event.Id);
            _onSavedOrDeleted?.Invoke();
            Close();
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            if (!Validate(out var error))
            {
                ValidationBar.Title = "לא ניתן לשמור";
                ValidationBar.Message = error;
                ValidationBar.IsOpen = true;
                return;
            }

            ApplyToEvent(_event);
            EventsRepository.AddOrUpdate(_event);
            _onSavedOrDeleted?.Invoke();
            Close();
        }

        // ─── Validation & extraction ───────────────────────────────────────────────

        private bool Validate(out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            { error = "יש להזין כותרת לאירוע."; return false; }

            var startDate = ResolveStartDate();
            if (startDate == null)
            { error = "יש לבחור תאריך תקין."; return false; }

            if (!AllDaySwitch.IsOn && StartTimePicker.Time == TimeSpan.Zero)
            {
                // Allow midnight, but if not all-day we expect *something*. Treat 0 as valid.
            }

            // Block reminders in the past relative to "now"
            var now = DateTimeOffset.Now;
            var loc = App.Settings.GetEffectiveLocation();
            var startTime = AllDaySwitch.IsOn ? (TimeSpan?)null : StartTimePicker.Time;
            var probeStart = startDate.Value.Date.Add(startTime ?? TimeSpan.Zero);

            foreach (var r in _reminders.Where(x => x.Enabled))
            {
                if (r.AnchorKind == ReminderAnchorKind.FixedOffset)
                {
                    var fire = probeStart.AddMinutes(r.OffsetMinutes);
                    if (new DateTimeOffset(fire) < now)
                    {
                        error = "אחת התזכורות נופלת בעבר. שנה את זמן האירוע או את ההיסט.";
                        return false;
                    }
                }
                else
                {
                    if (r.ZmanAnchors.Count == 0)
                    { error = "לכל תזכורת מסוג זמן הלכתי יש לבחור לפחות עוגן אחד."; return false; }

                    foreach (var anchor in r.ZmanAnchors)
                    {
                        var t = ReminderScheduler.ResolveZmanInstant(startDate.Value, anchor.Zman, loc);
                        if (t.HasValue && t.Value.AddMinutes(r.OffsetMinutes) < now &&
                            r.ZmanCombination == ZmanCombination.All)
                        {
                            error = "אחת התזכורות נופלת בעבר. שנה את זמן האירוע או את ההיסט.";
                            return false;
                        }
                    }
                    // For Earliest, only check the earliest:
                    if (r.ZmanCombination == ZmanCombination.Earliest)
                    {
                        var fires = r.ZmanAnchors
                            .Select(a => ReminderScheduler.ResolveZmanInstant(startDate.Value, a.Zman, loc))
                            .Where(x => x.HasValue)
                            .Select(x => x!.Value.AddMinutes(r.OffsetMinutes))
                            .ToList();
                        if (fires.Count > 0 && fires.Min() < now)
                        {
                            error = "אחת התזכורות נופלת בעבר. שנה את זמן האירוע או את ההיסט.";
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private DateTime? ResolveStartDate()
        {
            bool greg = (DateModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Greg";
            if (greg)
            {
                if (!GregDatePicker.Date.HasValue) return null;
                return GregDatePicker.Date.Value.DateTime.Date;
            }
            else
            {
                int? day   = TagAsInt(HebDayCombo);
                int? month = TagAsInt(HebMonthCombo);
                int year   = (int)HebYearBox.Value;
                if (!day.HasValue || !month.HasValue || year < 5000) return null;
                var g = HebcalBridge.ConvertFromHebrew(year, month.Value, day.Value);
                if (g == null || g.Year == 0) return null;
                return new DateTime(g.Year, g.Month, g.Day);
            }
        }

        private void ApplyToEvent(UserEvent ev)
        {
            ev.Title = TitleBox.Text.Trim();
            ev.Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();

            var startDate = ResolveStartDate()!.Value;
            ev.StartGregorian = startDate;
            // EventsRepository.FillMissingDate will fill StartHebrew on save.
            ev.StartHebrew = null;

            ev.StartTime = AllDaySwitch.IsOn ? null : StartTimePicker.Time;
            ev.Duration = (!AllDaySwitch.IsOn && !double.IsNaN(DurationMinutesBox.Value) && DurationMinutesBox.Value > 0)
                ? TimeSpan.FromMinutes(DurationMinutesBox.Value)
                : null;

            var recTag = (RecurrenceCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "None";
            if (recTag == "None")
            {
                ev.Recurrence = null;
            }
            else
            {
                var kind = Enum.Parse<RecurrenceKind>(recTag);
                ev.Recurrence = new RecurrenceRule
                {
                    Kind = kind,
                    Interval = (int)Math.Max(1, IntervalBox.Value),
                    Until = UntilPicker.Date?.DateTime,
                    Weekdays = kind == RecurrenceKind.WeeklyGregorian ? CollectWeekdayMask() : null
                };
            }

            ev.Reminders = _reminders.Select(CloneRule).ToList();
        }

        // ─── Reminders dynamic UI ──────────────────────────────────────────────────

        private void RebuildRemindersUi()
        {
            RemindersPanel.Children.Clear();
            for (int i = 0; i < _reminders.Count; i++)
                RemindersPanel.Children.Add(BuildReminderCard(_reminders[i], i));
        }

        private Border BuildReminderCard(ReminderRule rule, int index)
        {
            var sp = new StackPanel { Spacing = 6 };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var enabledCheck = new CheckBox
            {
                Content = $"תזכורת {index + 1}",
                IsChecked = rule.Enabled
            };
            enabledCheck.Checked   += (_, _) => rule.Enabled = true;
            enabledCheck.Unchecked += (_, _) => rule.Enabled = false;
            Grid.SetColumn(enabledCheck, 0);
            headerGrid.Children.Add(enabledCheck);

            var removeBtn = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 12 },
                Padding = new Thickness(8, 4, 8, 4)
            };
            removeBtn.Click += (_, _) =>
            {
                _reminders.RemoveAt(index);
                RebuildRemindersUi();
            };
            Grid.SetColumn(removeBtn, 1);
            headerGrid.Children.Add(removeBtn);
            sp.Children.Add(headerGrid);

            // Anchor type
            var anchorCombo = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Header = "סוג עוגן"
            };
            var fixedItem = new ComboBoxItem { Content = "זמן קבוע (יחסית להתחלה)", Tag = "Fixed" };
            var zmanItem  = new ComboBoxItem { Content = "זמן הלכתי", Tag = "Zman" };
            anchorCombo.Items.Add(fixedItem);
            anchorCombo.Items.Add(zmanItem);
            anchorCombo.SelectedIndex = rule.AnchorKind == ReminderAnchorKind.Zman ? 1 : 0;
            sp.Children.Add(anchorCombo);

            // Offset
            var offsetGrid = new Grid();
            offsetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            offsetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var offsetBox = new NumberBox
            {
                Header = "היסט (דק'; שלילי = לפני)",
                Value = rule.OffsetMinutes,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Margin = new Thickness(0, 0, 4, 0)
            };
            offsetBox.ValueChanged += (_, e) =>
            {
                if (!double.IsNaN(e.NewValue)) rule.OffsetMinutes = (int)e.NewValue;
            };
            Grid.SetColumn(offsetBox, 0);
            offsetGrid.Children.Add(offsetBox);

            var combineCombo = new ComboBox
            {
                Header = "מצרף עוגנים",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(4, 0, 0, 0)
            };
            combineCombo.Items.Add(new ComboBoxItem { Content = "המוקדם ביותר", Tag = "Earliest" });
            combineCombo.Items.Add(new ComboBoxItem { Content = "כל אחד בנפרד", Tag = "All" });
            combineCombo.SelectedIndex = rule.ZmanCombination == ZmanCombination.All ? 1 : 0;
            combineCombo.SelectionChanged += (_, _) =>
            {
                var tag = (combineCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                rule.ZmanCombination = tag == "All" ? ZmanCombination.All : ZmanCombination.Earliest;
            };
            Grid.SetColumn(combineCombo, 1);
            offsetGrid.Children.Add(combineCombo);
            sp.Children.Add(offsetGrid);

            // Zman anchors panel
            var zmanPanel = new StackPanel { Spacing = 4 };
            void RebuildZmanPanel()
            {
                zmanPanel.Children.Clear();
                for (int i = 0; i < rule.ZmanAnchors.Count; i++)
                {
                    int idx = i;
                    var rowGrid = new Grid();
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var zCombo = BuildZmanCombo(rule.ZmanAnchors[idx].Zman, k => rule.ZmanAnchors[idx].Zman = k);
                    Grid.SetColumn(zCombo, 0);
                    rowGrid.Children.Add(zCombo);
                    var rmBtn = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, Margin = new Thickness(4, 0, 0, 0) };
                    rmBtn.Click += (_, _) => { rule.ZmanAnchors.RemoveAt(idx); RebuildZmanPanel(); };
                    Grid.SetColumn(rmBtn, 1);
                    rowGrid.Children.Add(rmBtn);
                    zmanPanel.Children.Add(rowGrid);
                }
                var addBtn = new Button { Content = "+ הוסף עוגן הלכתי", HorizontalAlignment = HorizontalAlignment.Stretch };
                addBtn.Click += (_, _) =>
                {
                    rule.ZmanAnchors.Add(new ZmanimAnchor { Zman = ZmanimKey.Sunrise });
                    RebuildZmanPanel();
                };
                zmanPanel.Children.Add(addBtn);
            }
            RebuildZmanPanel();
            sp.Children.Add(zmanPanel);

            void ApplyAnchorVisibility()
            {
                bool isZman = rule.AnchorKind == ReminderAnchorKind.Zman;
                zmanPanel.Visibility = isZman ? Visibility.Visible : Visibility.Collapsed;
                combineCombo.Visibility = isZman ? Visibility.Visible : Visibility.Collapsed;
            }
            anchorCombo.SelectionChanged += (_, _) =>
            {
                var tag = (anchorCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                rule.AnchorKind = tag == "Zman" ? ReminderAnchorKind.Zman : ReminderAnchorKind.FixedOffset;
                ApplyAnchorVisibility();
            };
            ApplyAnchorVisibility();

            return new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                Child = sp
            };
        }

        private static ComboBox BuildZmanCombo(ZmanimKey current, Action<ZmanimKey> onChange)
        {
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            int selected = 0;
            int i = 0;
            foreach (ZmanimKey k in Enum.GetValues<ZmanimKey>())
            {
                combo.Items.Add(new ComboBoxItem { Content = ReminderScheduler.GetZmanLabel(k), Tag = k });
                if (k == current) selected = i;
                i++;
            }
            combo.SelectedIndex = selected;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem item && item.Tag is ZmanimKey k)
                    onChange(k);
            };
            return combo;
        }

        // ─── Helpers ───────────────────────────────────────────────────────────────

        private DaysOfWeek CollectWeekdayMask()
        {
            DaysOfWeek mask = DaysOfWeek.None;
            if (ChkSun.IsChecked == true) mask |= DaysOfWeek.Sunday;
            if (ChkMon.IsChecked == true) mask |= DaysOfWeek.Monday;
            if (ChkTue.IsChecked == true) mask |= DaysOfWeek.Tuesday;
            if (ChkWed.IsChecked == true) mask |= DaysOfWeek.Wednesday;
            if (ChkThu.IsChecked == true) mask |= DaysOfWeek.Thursday;
            if (ChkFri.IsChecked == true) mask |= DaysOfWeek.Friday;
            if (ChkSat.IsChecked == true) mask |= DaysOfWeek.Saturday;
            return mask;
        }

        private void ApplyWeekdayMask(DaysOfWeek mask)
        {
            ChkSun.IsChecked = mask.HasFlag(DaysOfWeek.Sunday);
            ChkMon.IsChecked = mask.HasFlag(DaysOfWeek.Monday);
            ChkTue.IsChecked = mask.HasFlag(DaysOfWeek.Tuesday);
            ChkWed.IsChecked = mask.HasFlag(DaysOfWeek.Wednesday);
            ChkThu.IsChecked = mask.HasFlag(DaysOfWeek.Thursday);
            ChkFri.IsChecked = mask.HasFlag(DaysOfWeek.Friday);
            ChkSat.IsChecked = mask.HasFlag(DaysOfWeek.Saturday);
        }

        private static void SelectByTag(ComboBox combo, object tag)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem ci && Equals(ci.Tag?.ToString(), tag?.ToString()))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private static int? TagAsInt(ComboBox combo)
        {
            if (combo.SelectedItem is ComboBoxItem ci && ci.Tag is int i) return i;
            return null;
        }
    }
}
