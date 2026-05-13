using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ItimHebrewCalendar.Models;

namespace ItimHebrewCalendar.Services
{
    public static class EventsRepository
    {
        private static readonly string EventsPath = Path.Combine(
            SettingsManager.GetAppDir(), "events.json");

        private static readonly object Lock = new();
        private static List<UserEvent> _events = new();
        private static bool _loaded;

        public static event Action? Changed;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static IReadOnlyList<UserEvent> All
        {
            get
            {
                EnsureLoaded();
                lock (Lock) return _events.ToList();
            }
        }

        public static UserEvent? GetById(Guid id)
        {
            EnsureLoaded();
            lock (Lock) return _events.FirstOrDefault(e => e.Id == id);
        }

        public static void AddOrUpdate(UserEvent ev)
        {
            EnsureLoaded();
            FillMissingDate(ev);
            ev.ModifiedUtc = DateTime.UtcNow;

            lock (Lock)
            {
                var existing = _events.FindIndex(e => e.Id == ev.Id);
                if (existing >= 0) _events[existing] = ev;
                else _events.Add(ev);
                Save();
            }
            Changed?.Invoke();
        }

        public static void Delete(Guid id)
        {
            EnsureLoaded();
            lock (Lock)
            {
                var idx = _events.FindIndex(e => e.Id == id);
                if (idx < 0) return;
                _events.RemoveAt(idx);
                Save();
            }
            Changed?.Invoke();
        }

        // Bulk import (used by ICS importer). Skips events whose Id already exists.
        public static int ImportMany(IEnumerable<UserEvent> events)
        {
            EnsureLoaded();
            int added = 0;
            lock (Lock)
            {
                foreach (var ev in events)
                {
                    if (_events.Any(e => e.Id == ev.Id)) continue;
                    FillMissingDate(ev);
                    ev.IsImported = true;
                    ev.ModifiedUtc = DateTime.UtcNow;
                    _events.Add(ev);
                    added++;
                }
                if (added > 0) Save();
            }
            if (added > 0) Changed?.Invoke();
            return added;
        }

        private static void EnsureLoaded()
        {
            lock (Lock)
            {
                if (_loaded) return;
                _loaded = true;
                try
                {
                    if (!File.Exists(EventsPath))
                    {
                        _events = new List<UserEvent>();
                        return;
                    }
                    var json = File.ReadAllText(EventsPath);
                    var loaded = JsonSerializer.Deserialize<List<UserEvent>>(json, JsonOpts);
                    _events = loaded ?? new List<UserEvent>();
                    foreach (var ev in _events) FillMissingDate(ev);
                }
                catch (Exception ex)
                {
                    SettingsManager.LogError("EventsRepository.Load", ex);
                    _events = new List<UserEvent>();
                }
            }
        }

        private static void Save()
        {
            try
            {
                var dir = SettingsManager.GetAppDir();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_events, JsonOpts);
                File.WriteAllText(EventsPath, json);
            }
            catch (Exception ex)
            {
                SettingsManager.LogError("EventsRepository.Save", ex);
            }
        }

        // Fills the missing side of the (Gregorian, Hebrew) pair using HebcalBridge,
        // so consumers can rely on either representation regardless of input mode.
        private static void FillMissingDate(UserEvent ev)
        {
            try
            {
                if (ev.StartGregorian == null && ev.StartHebrew != null)
                {
                    var g = HebcalBridge.ConvertFromHebrew(
                        ev.StartHebrew.Year, ev.StartHebrew.Month, ev.StartHebrew.Day);
                    if (g != null)
                        ev.StartGregorian = new DateTime(g.Year, g.Month, g.Day);
                }
                else if (ev.StartHebrew == null && ev.StartGregorian != null)
                {
                    var h = HebcalBridge.Convert(ev.StartGregorian.Value);
                    if (h != null)
                        ev.StartHebrew = new HebrewDateRef
                        {
                            Year  = h.HebYear,
                            Month = h.HebMonth,
                            Day   = h.HebDay
                        };
                }
            }
            catch (Exception ex)
            {
                SettingsManager.LogError("EventsRepository.FillMissingDate", ex);
            }
        }
    }
}
