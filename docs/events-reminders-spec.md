# איפיון: מערכת אירועים ותזכורות מבוססת זמני היום

> **סטטוס:** מאושר לפיתוח · **תאריך:** 2026-04-26 · **יעד:** Itim Hebrew Calendar

## החלטות שנסגרו (עדכון 2026-04-26)
1. **Google Calendar — מחוץ לסקופ.** לא משאירים hooks אגרסיביים, אבל `ExternalSyncRef` נשמר במודל לשימוש עתידי.
2. **Windows Calendar — סנכרון רק לאירועים ללא עוגן הלכתי.** אירועים עם עוגן `Zman` מסומנים ב-UI כ"מקומי בלבד".
3. **תצוגות יומית/חודשית** נוספות גם ב-`MainWindow` וגם ב-`CalendarPopup` (Tray) עם מתג ToggleSwitch/SegmentedControl. שתי הגדרות חדשות ב-`AppSettings`: `DefaultMainView`, `DefaultTrayView` (ערכים: `Monthly` | `Daily`).
4. **תזכורות שהוחמצו** (תוכנה הייתה כבויה): מציגים סקירה מאוחדת בעלייה הבאה, עד 24 שעות אחורה.
5. **חזרתיות עברית בחודש שאינו קיים** (ל' חסר וכו'): ברירת מחדל = **דלג**.
6. **מספר תזכורות באותו רגע** (חלון 60 שניות): **toast יחיד** עם רשימת התזכורות בגוף ההודעה.
7. **תזכורת בזמן עבר** במהלך יצירה/עריכה: **חסום** (UI יציג שגיאה, לא יאפשר שמירה).
8. **תזכורת אגרגטיבית לזמן הלכתי**: כששתי תזכורות זמן-יום (אירוע + עצמאית) מתעוררות באותו רגע — מאוחדות לפי כלל #6 לעיל, עם ציון העוגן בגוף.
9. **ייבוא ICS**: כן. אירועים מיובאים נכנסים כעוגן `FixedTime` (כי ICS לא מכיר עוגן הלכתי), עם תווית "מיובא" אופציונלית.
10. **ייצוא ICS**: כן. אירועים עם עוגן הלכתי מיוצאים כשעה מקובעת לכל הופעה ב-N חודשים קדימה (ברירת מחדל 12).

---

## 1. רקע ומטרות

כיום `ItimHebrewCalendar` מציג לוח שנה עברי, חגים, פרשות וזמני היום ההלכתיים, אך אינו מאפשר למשתמש להוסיף אירועים אישיים או לקבל התראות. האפיון הזה מגדיר מערכת המאפשרת:

1. **הוספת אירועים אישיים ליומן** (חד-פעמיים או חוזרים) עם תזכורות.
2. **הצמדת זמן תזכורת לאירוע** לזמן הלכתי כלשהו (זריחה, חצות, שקיעה וכו') במקום לשעה קבועה.
3. **תזכורות עצמאיות לזמני היום** ללא קשר לאירוע משתמש (למשל "התרע אותי 15 דק' לפני סוף זמן ק"ש כל יום").
4. **סנכרון אופציונלי** ליומן Windows ו/או ליומן Google.

המטרה הייחודית של הפיצ'ר היא לגשר על הפער בין יומנים סטנדרטיים (שעה קבועה בלבד) לבין הצורך הדתי בעיגון לזמן הלכתי שמשתנה מדי יום לפי מיקום ותאריך לועזי.

---

## 2. הגדרות ומונחים

| מונח | משמעות |
|---|---|
| **אירוע (Event)** | פריט יומן שהמשתמש יצר. מכיל כותרת, תאריך/שעה, חזרתיות, ואפס או יותר תזכורות. |
| **תזכורת (Reminder)** | שיגור התראה למשתמש בנקודת זמן מחושבת. שייכת לאירוע **או** עצמאית (קשורה לזמן הלכתי). |
| **עוגן (Anchor)** | בסיס חישוב הזמן: שעה קבועה (`FixedTime`) או מזהה זמן הלכתי (`Zman`, למשל `SofZmanShma`). |
| **היסט (Offset)** | מספר דקות לפני/אחרי/בזמן העוגן (יכול להיות שלילי, חיובי או 0). |
| **מקור הזמנים** | `KosherJava` או `Hebcal` — נקבע ב-`AppSettings.ZmanimSource`. |

---

## 3. דרישות פונקציונליות

### 3.1 ניהול אירועים אישיים
- יצירה / עריכה / מחיקה של אירוע מתוך:
  - חלון יום (`DayDetailsWindow`) — כפתור "אירוע חדש".
  - תפריט הקשר על יום בלוח החודשי (`MainWindow` / `CalendarPopup`).
- שדות אירוע:
  - כותרת (חובה).
  - תיאור חופשי (אופציונלי, רב-שורתי).
  - תאריך התחלה (לועזי **או** עברי — שני הקלטים מתורגמים זה לזה ב-UI).
  - שעת התחלה (אופציונלית — אם חסרה: "כל היום").
  - משך / שעת סיום (אופציונלית).
  - חזרתיות (ראו 3.2).
  - רשימת תזכורות (ראו 3.3).
  - צבע / תווית (לסידור ויזואלי בלוח).
- אינדיקציה ויזואלית של אירועי משתמש בלוח החודשי, בנפרד מאירועי החג של hebcal-go.

### 3.2 חזרתיות
תמיכה בארבעה סוגים, כולם יכולים לקבל **תאריך סיום** או "ללא הגבלה":
1. **חד-פעמי**.
2. **חוזר לועזי** — יומי / שבועי בימים נבחרים / חודשי לועזי / שנתי לועזי.
3. **חוזר עברי** — ה-X בחודש עברי, יום השנה (יארצייט), ראש חודש.
4. **חזרתיות הלכתית** — נקשר ישירות לעוגן (ראו 3.3.2). למשל "כל יום בעלות השחר".

טיפול ב-**שנה מעוברת** וב-**ל' בחודש מלא/חסר** עבור חזרתיות עברית: ברירת מחדל לדחות ליום הבא הזמין, עם הגדרה ל"דלג" אם המשתמש מעדיף.

### 3.3 תזכורות

#### 3.3.1 תזכורת עם עוגן זמן קבוע
- היסט בדקות לפני / אחרי / בזמן ההתחלה.
- יכולות להיות מספר תזכורות לכל אירוע (למשל יום קודם + שעה קודם).

#### 3.3.2 תזכורת עם עוגן זמן הלכתי
- בחירה מתוך רשימת זמני היום של היום שבו האירוע חל:
  `AlotHaShachar`, `Misheyakir`, `MisheyakirMachmir`, `Sunrise`, `SofZmanShmaMGA`, `SofZmanShma`, `SofZmanTfillaMGA`, `SofZmanTfilla`, `Chatzot`, `MinchaGedola`, `MinchaKetana`, `PlagHaMincha`, `Sunset`, `Tzeit`, `Tzeit72`, `CandleLighting18`.
- היסט: **לפני / אחרי / בדיוק בזמן** ב-X דקות (X ≥ 0).
- ניתן לבחור **יותר מעוגן אחד** לאותה תזכורת — תופעל באירוע הראשון מבין כולם, או בכל אחד בנפרד (אופציה ב-UI: "כולם" / "המוקדם ביותר").
- כשהזמן ההלכתי לא קיים בתאריך מסוים (למשל אזורי קוטב — נדיר אבל אפשרי) — תזכורת מדולגת ונרשמת ל-`errors.log`.

#### 3.3.3 תזכורת עצמאית לזמן הלכתי (ללא אירוע משתמש)
- ניתנת להגדרה ב-Settings תחת אזור חדש: **"תזכורות לזמני היום"**.
- כל ערך: זמן הלכתי + היסט + תדירות (כל יום / רק בימי חול / רק בשבת ויו"ט / ימים מסוימים בשבוע).
- מנוהלת בנפרד ממערכת האירועים, אך משתמשת באותו תשתית התראות (ראו §5.2).

### 3.4 הצגה ואינטראקציה
- **בלוח החודשי**: נקודה / סמל קטן ביום שיש בו אירוע משתמש; tooltip עם רשימה.
- **בחלון יום (`DayDetailsWindow`)**: סקציה "אירועים אישיים" עם רשימת היום + כפתור הוספה.
- **חלון "אירועים קרובים"** (חדש, אופציונלי): רשימת ה-N האירועים הבאים, עם פילטר לפי תווית.
- **בעת התראה**: הודעת toast של Windows (Notifications API דרך `H.NotifyIcon` או `Microsoft.Toolkit.Uwp.Notifications`) עם:
  - כותרת האירוע.
  - הזמן ההלכתי שאליו הוצמד (אם רלוונטי) + הזמן בפועל.
  - פעולות מהירות: "נודה" / "דחה ב-10 דק'" / "פתח אירוע".

---

## 4. מודל נתונים

### 4.1 Persistence
שימוש בקובץ נפרד מ-`settings.json` כדי לא להעמיס על קונפיגורציה:
```
%APPDATA%\ItimHebrewCalendar\events.json
```
מבוסס JSON, נטען ע"י שירות חדש `EventsRepository` בדומה ל-`SettingsManager`.
שיקול: אם קצב הכתיבה גבוה (סנכרון תכוף) — לעבור ל-SQLite. בשלב ראשון JSON מספיק.

### 4.2 סכמת אירוע (C#)
```csharp
public class UserEvent
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }

    // אחד מהשניים מוגדר; השני נגזר ב-load
    public DateTime? StartGregorian { get; set; }
    public HebrewDateRef? StartHebrew { get; set; }

    public TimeSpan? StartTime { get; set; }   // null = כל היום
    public TimeSpan? Duration { get; set; }

    public RecurrenceRule? Recurrence { get; set; }
    public List<ReminderRule> Reminders { get; set; } = new();

    public string? ColorTag { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    // לסנכרון חיצוני (ראו §6)
    public ExternalSyncRef? ExternalRef { get; set; }
}
```

### 4.3 כללי תזכורת
```csharp
public class ReminderRule
{
    public Guid Id { get; set; }
    public ReminderAnchorKind AnchorKind { get; set; }   // FixedOffset | Zman
    public List<ZmanimAnchor> ZmanAnchors { get; set; } = new();   // 1+ כשKind=Zman
    public ZmanCombination ZmanCombination { get; set; }  // Earliest | All
    public int OffsetMinutes { get; set; }   // שלילי = לפני, חיובי = אחרי, 0 = בזמן
    public bool Enabled { get; set; } = true;
}

public enum ReminderAnchorKind { FixedOffset, Zman }
public enum ZmanCombination { Earliest, All }

public class ZmanimAnchor
{
    public ZmanimKey Zman { get; set; }   // enum המתאים לשדות ZmanimInfo
}
```

### 4.4 חזרתיות
```csharp
public class RecurrenceRule
{
    public RecurrenceKind Kind { get; set; }
    // יומי/שבועי/חודשי-לועזי/שנתי-לועזי/חודשי-עברי/שנתי-עברי
    public int Interval { get; set; } = 1;       // כל N יחידות
    public DaysOfWeek? Weekdays { get; set; }    // אם Weekly
    public LeapMonthPolicy LeapPolicy { get; set; }   // לעברי בלבד
    public DateTime? Until { get; set; }
    public int? Count { get; set; }
}
```

### 4.5 תזכורת עצמאית לזמן הלכתי (Settings)
מורחב לתוך `AppSettings`:
```csharp
public List<StandaloneZmanReminder> StandaloneZmanReminders { get; set; } = new();

public class StandaloneZmanReminder
{
    public Guid Id { get; set; }
    public string Label { get; set; } = "";   // למשל "סוף זמן ק"ש"
    public ZmanimKey Zman { get; set; }
    public int OffsetMinutes { get; set; }
    public DaysOfWeek ActiveDays { get; set; } = DaysOfWeek.All;
    public bool SkipShabbatYomTov { get; set; }
    public bool Enabled { get; set; } = true;
}
```

---

## 5. רכיבים טכניים

### 5.1 שירותים חדשים
| שירות | אחריות |
|---|---|
| `EventsRepository` | קריאה/כתיבה ל-`events.json`, פתרון התנגשויות סנכרון, מתן API CRUD. |
| `EventOccurrenceExpander` | בהינתן `UserEvent`, מחזיר את ההופעות בטווח תאריכים נתון (פתיחה של חזרתיות). |
| `ReminderScheduler` | מקבל הופעת אירוע + `ReminderRule` ומחזיר זמן התזכורת בפועל (DateTimeOffset). יודע לקרוא ל-`ZmanimService` עבור עוגנים הלכתיים. |
| `NotificationDispatcher` | רושם התראות ל-Windows Notifications, מטפל באקשנים (snooze/dismiss). |
| `ReminderHostService` | משימת רקע שרצה כל עוד התהליך פעיל; טוענת את כל התזכורות ל-24 שעות הקרובות, מציבה `Timer`-ים, מתעוררת ב-midnight או בשינוי הגדרות לרענון. |

### 5.2 חישוב זמן תזכורת
פונקציית הליבה (פסאודו-קוד):
```
ResolveReminderTime(occurrence, rule, location):
    if rule.AnchorKind == FixedOffset:
        return occurrence.Start + rule.OffsetMinutes
    
    zmanim = ZmanimService.Get(occurrence.Date, location)
    candidates = []
    for anchor in rule.ZmanAnchors:
        zmanTime = zmanim[anchor.Zman]
        if zmanTime == null: continue
        candidates.add(zmanTime + rule.OffsetMinutes)
    
    if rule.ZmanCombination == Earliest:
        return min(candidates)   // יחיד
    else:
        return candidates        // יורה כמה התראות
```

### 5.3 התאמה לאזור זמן ולשינויים מקומיים
- כל החישובים נעשים ב-TimeZone של ה-`LocationInfo` של המשתמש (ולא TimeZone המערכת), כי הזמנים ההלכתיים תלויים בו.
- שינוי עיר ב-Settings ⇒ `ReminderHostService.RescheduleAll()`.
- שעון קיץ ⇒ נטפל אוטומטית ע"י המרה ל-`DateTimeOffset` ושמירה ב-UTC ברגע ההצבה.

### 5.4 אמינות
- שמירת לוג אחרון של תזכורות שהופעלו ב-`%APPDATA%\ItimHebrewCalendar\reminders.log` (לדיבוג; עם rotation אחרי 1MB).
- בעת עליה: סריקה רטרואקטיבית של תזכורות שהיו אמורות להפעיל ב-X הדקות האחרונות (ברירת מחדל 5) — אם התוכנה הייתה כבויה — מציגות "החמצה" אחרת (toast מצטבר).
- תלות ב`StartWithWindows` שכבר קיים — אם המשתמש מכבה את האפליקציה, אין מי שייצר התראות; להבליט זאת ב-UI.

---

## 6. סנכרון ליומנים חיצוניים

זה החלק המורכב ביותר ויש לו השלכות תכנון. שני המסלולים אינם שווי-עלות:

### 6.1 יומן Windows (User Calendar / `Windows.ApplicationModel.Appointments`)
**יתרונות:**
- API מובנה ב-Windows App SDK — אין צורך ב-OAuth.
- האירועים מופיעים ב-Calendar app הסטנדרטי, ב-Outlook, ובכל לקוח שמתחבר ל-User Data.

**מגבלות קריטיות לפיצ'ר:**
- האירוע נשמר עם **שעה קבועה בלבד**. אין דרך להגיד "התרע 10 דק' לפני זריחה" ב-API הסטנדרטי.
- פתרון: בעת היצירה, לחשב את הזמן בפועל ליום של האירוע ולשמור אותו. **בכל יום של חזרתיות הלכתית — צריך לעדכן את האירוע מחדש**, כי הזריחה משתנה.
- כך גם התזכורת: יומן Windows יורה התראה בעצמו לפי השעה ששמרנו, לא לפי הזמן ההלכתי.

**המלצה:**
- בשלב ראשון: **לא לסנכרן** אירועים עם עוגן הלכתי ליומן Windows.
- לסנכרן רק אירועים עם עוגן `FixedTime` ושעה קבועה.
- להציג ב-UI סימון ברור: "אירוע זה אינו נסנכרן ליומן המערכת בשל עוגן הלכתי".

### 6.2 יומן Google
**יתרונות:**
- API עשיר עם תמיכה בעדכון תכוף (יחשבן את הזמן ההלכתי כל יום ויעדכן את האירוע).
- המשתמש יכול לקבל התראות גם בנייד.

**עלויות ומורכבות:**
- דורש OAuth 2.0 + הקמת פרויקט ב-Google Cloud Console + client_id ייעודי לאפליקציה.
- צריך טיפול ב-refresh token ב-`SecureStorage` (Windows Credential Locker).
- צריך גרסה משלנו של "loop יומי" שעובר על כל אירוע הלכתי ומעדכן את ה-`startTime` ב-Google Calendar API.
- conflict resolution אם המשתמש ערך באירוע ב-Google.

**המלצה לשלב ראשון:**
- **לא לכלול בגרסה הראשונה.** לשמור hooks בקוד (`ExternalSyncRef`) כדי שיהיה קל להוסיף בעתיד.
- בגרסה השנייה: "יצוא חד-פעמי" (לחצן "ייצא לגוגל" שיוצר אירועים אחת ואחר כך לא מתחזק אותם) — קל יחסית ומועיל למקרים פשוטים.
- בגרסה שלישית: סנכרון דו-כיווני מלא עם רענון יומי.

### 6.3 ICS (Export/Subscribe)
אופציה שלישית **שלא נשאלה אבל ראויה לשקילה**: לחשוף URL/קובץ `.ics` שמתעדכן עם כל האירועים הצפויים ל-X חודשים קדימה (כולל זמני הלכה כבר מחושבים). המשתמש יכול לעשות "Subscribe" ב-Google/Outlook/Apple Calendar, וההתראה תגיע מהלקוח שלו. יתרון: עוקפים את כל ה-OAuth. חיסרון: רענון אטי (12-24 שעות בד"כ) ועוגן הלכתי "מוקפא" לרגע הייצוא.

---

## 7. UI — שינויים נדרשים

### 7.1 חלון הגדרות (`SettingsWindow.xaml`)
- Expander חדש: **"תזכורות לזמני היום"** — רשימת `StandaloneZmanReminder` עם הוספה/עריכה/מחיקה.
- Expander חדש: **"סנכרון יומנים"** — checkbox ל-User Calendar; כפתור "התחבר ל-Google" (כשייושם).

### 7.2 חלון יום (`DayDetailsWindow.xaml`)
- סקציה חדשה: **"אירועים אישיים"** — רשימה + כפתור "+ הוסף".

### 7.3 חלון עריכת אירוע (חדש: `EventEditorWindow.xaml`)
- שדות לפי §3.1.
- בלוק **"תזכורות"** הניתן להוספה דינמית; כל תזכורת מציעה החלפה בין עוגן קבוע לעוגן הלכתי.
- בורר זמן הלכתי: ComboBox עם תרגום עברי + תצוגה מקדימה של הזמן בפועל ליום שנבחר.

### 7.4 לוח חודשי (`MainWindow` / `CalendarPopup`)
- נקודה צבעונית קטנה ביום שיש בו אירוע משתמש (עם הצבע של ה-tag).
- tooltip עם כותרות.

---

## 8. אבטחה ופרטיות
- אירועי משתמש עלולים להיות אישיים — לוודא ש-`events.json` נשמר ב-`%APPDATA%` של המשתמש בלבד (כבר כך, בזכות `SpecialFolder.ApplicationData`).
- טוקנים של Google Calendar (כשיתווסף): שמירה ב-Windows Credential Locker (`PasswordVault`), לא בקובץ JSON.
- ההתראות לא יציגו את התיאור המלא בפרסומים נעולים אם ההגדרה "פרטיות במסך נעול" סומנה (אופציונלי).

---

## 9. שלבי פיתוח מומלצים

### Phase 1 — תשתית מקומית בלבד
- מודל נתונים (`UserEvent`, `ReminderRule`, `RecurrenceRule`).
- `EventsRepository` + `EventOccurrenceExpander`.
- `ReminderScheduler` + `ReminderHostService` + `NotificationDispatcher`.
- `EventEditorWindow` עם תמיכה בעוגן קבוע ועוגן הלכתי.
- שילוב ב-`DayDetailsWindow` ובלוח החודשי.

### Phase 2 — תזכורות עצמאיות לזמני היום
- מודל `StandaloneZmanReminder`.
- UI ב-Settings.
- שילוב ב-`ReminderHostService` (אותו pipeline).

### Phase 3 — ייצוא בסיסי
- ייצוא ICS חד-פעמי (כפתור "שמור כקובץ יומן").
- אופציונלי: סנכרון חד-כיווני ל-Windows User Calendar עבור אירועים ללא עוגן הלכתי.

### Phase 4 — אינטגרציות חיצוניות
- Google Calendar: OAuth + סנכרון יומי.
- אופציונלי: subscription ICS דרך שרת קל (פחות מומלץ — דורש hosting).

---

## 10. שאלות פתוחות
1. **המשתמש כיבה את האפליקציה** — האם להציג סקירה של תזכורות שהוחמצו בעת ההפעלה הבאה? האם לאפשר השלמה רק עד X זמן אחרי?
2. **התנגשויות תאריך עברי** — בשנה מעוברת, אירוע ביום ה-30 בחודש שאינו קיים — דיפולט "דחה ליום הבא" או "דלג"?
3. **מספר תזכורות באותו רגע** — לאחד ל-toast אחד ("3 תזכורות") או ליצור 3 toasts?
4. **תזכורת בזמן עבר** — בשלב היצירה לחסום או רק להזהיר?
5. **התראה אחת לכל זמן הלכתי** או אגרגציה ("הגיעה זריחה — 3 תזכורות פעילות")?
6. **יבוא** — האם רוצים גם יבוא של אירועים מ-ICS או מ-Google? (כיוון הפוך מ-§6).

---

## 11. אומדן מאמץ גס

| Phase | אומדן | תלויות |
|---|---|---|
| 1 | 5–8 ימי פיתוח | אין (משתמש בתשתית הקיימת של `ZmanimService` ו-`SettingsManager`) |
| 2 | 1–2 ימים | Phase 1 |
| 3 (ICS export) | 1 יום | Phase 1 |
| 3 (Windows Calendar) | 2–3 ימים | Phase 1 + הבנת מגבלות `Appointments` API |
| 4 (Google) | 5–7 ימים | Phase 1 + הקמת פרויקט Google Cloud |

---

**הצעד הבא המומלץ:** סגירה על השאלות הפתוחות בסעיף 10, ואז התחלת Phase 1 עם מודל הנתונים ו-`EventsRepository`.
