using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// 1. تشغيل ملفات الموقع من فولدر wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

// 2. مسار ملف قاعدة البيانات
string dbFilePath = Path.Combine(app.Environment.ContentRootPath, "alnahar_database.json");

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
};

AppDatabase LoadDb()
{
    if (!File.Exists(dbFilePath))
    {
        var initialDb = new AppDatabase();
        SaveDb(initialDb);
        return initialDb;
    }
    try
    {
        string json = File.ReadAllText(dbFilePath);
        return JsonSerializer.Deserialize<AppDatabase>(json, jsonOptions) ?? new AppDatabase();
    }
    catch
    {
        return new AppDatabase();
    }
}

void SaveDb(AppDatabase db)
{
    File.WriteAllText(dbFilePath, JsonSerializer.Serialize(db, jsonOptions));
}

string GetTodayKey() => DateTime.Now.ToString("yyyy-MM-dd");

// ==========================================
// 3. نقاط اتصال السيرفر (API Endpoints)
// ==========================================

// أ) جلب كل بيانات الموقع والإحصائيات اليومية
app.MapGet("/api/data", () =>
{
    var db = LoadDb();
    string today = GetTodayKey();

    int todayVisits = db.DailyVisits.ContainsKey(today) ? db.DailyVisits[today] : 0;

    // تحديث عداد اليوم لكل عميل للعرض في لوحة التحكم
    var customersList = db.Customers.Select(c => new
    {
        c.Name,
        c.Email,
        c.Phone,
        c.Address,
        c.Company,
        c.CreatedAt,
        todayVisits = (c.LastVisitDate == today) ? c.TodayVisits : 0
    }).ToList();

    return Results.Ok(new
    {
        fabrics = db.Fabrics,
        machines = db.Machines,
        batches = db.Batches,
        orders = db.Orders,
        customers = customersList,
        totalCustomers = db.Customers.Count,
        todayVisits = todayVisits
    });
});

// ب) تسجيل زيارة جديدة للموقع (يحسب زيارات اليوم العامة وزيارات العميل المسجل)
app.MapPost("/api/track-visit", (VisitTrackReq req) =>
{
    var db = LoadDb();
    string today = GetTodayKey();

    if (!db.DailyVisits.ContainsKey(today))
        db.DailyVisits[today] = 0;

    db.DailyVisits[today]++;

    if (!string.IsNullOrWhiteSpace(req.Email))
    {
        string cleanEmail = req.Email.Trim().ToLower();
        var cust = db.Customers.FirstOrDefault(x => x.Email.Trim().ToLower() == cleanEmail);
        if (cust != null)
        {
            if (cust.LastVisitDate == today)
                cust.TodayVisits++;
            else
            {
                cust.LastVisitDate = today;
                cust.TodayVisits = 1;
            }
        }
    }

    SaveDb(db);
    return Results.Ok(new { todayVisits = db.DailyVisits[today] });
});

// ج) دخول لوحة تحكم الإدارة
app.MapPost("/api/login", (LoginReq req) =>
{
    var db = LoadDb();
    if (req.Password == db.AdminPassword)
        return Results.Ok(new { success = true });

    return Results.Unauthorized();
});

// د) تحديث أسعار الأقمشة
app.MapPost("/api/fabrics", (List<JsonElement> fabrics) =>
{
    var db = LoadDb();
    db.Fabrics = fabrics;
    SaveDb(db);
    return Results.Ok(new { message = "تم حفظ أسعار الأقمشة" });
});

// هـ) تحديث الماكينات
app.MapPost("/api/machines", (List<JsonElement> machines) =>
{
    var db = LoadDb();
    db.Machines = machines;
    SaveDb(db);
    return Results.Ok(new { message = "تم حفظ الماكينات" });
});

// و) تحديث مراحل التشغيلات
app.MapPost("/api/batches", (Dictionary<string, JsonElement> batches) =>
{
    var db = LoadDb();
    db.Batches = batches;
    SaveDb(db);
    return Results.Ok(new { message = "تم حفظ التشغيلات" });
});

// ز) تغيير باسورد الإدارة
app.MapPost("/api/password", (LoginReq req) =>
{
    var db = LoadDb();
    db.AdminPassword = req.Password;
    SaveDb(db);
    return Results.Ok(new { message = "تم تحديث كلمة المرور" });
});

// ح) حفظ طلب شراء أو حجز ماكينة جديد
app.MapPost("/api/orders", (JsonElement newOrder) =>
{
    var db = LoadDb();
    db.Orders.Insert(0, newOrder);
    SaveDb(db);
    return Results.Ok(new { message = "تم حفظ الطلب" });
});

// ط) إنشاء حساب عميل جديد وحفظه في السيرفر
app.MapPost("/api/customers/register", (CustomerAccount c) =>
{
    if (string.IsNullOrWhiteSpace(c.Email) || string.IsNullOrWhiteSpace(c.Password))
        return Results.BadRequest(new { message = "يرجى إدخال البريد الإلكتروني وكلمة المرور" });

    var db = LoadDb();
    string cleanEmail = c.Email.Trim().ToLower();
    string today = GetTodayKey();

    bool exists = db.Customers.Any(x => !string.IsNullOrWhiteSpace(x.Email) && x.Email.Trim().ToLower() == cleanEmail);
    if (exists)
        return Results.Conflict(new { message = "البريد الإلكتروني مسجل مسبقاً، يرجى تسجيل الدخول" });

    c.Email = cleanEmail;
    c.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd hh:mm tt");
    c.LastVisitDate = today;
    c.TodayVisits = 1;

    db.Customers.Add(c);
    SaveDb(db);

    return Results.Ok(new { success = true, customer = c });
});

// ي) تسجيل دخول العميل
app.MapPost("/api/customers/login", (CustomerLoginReq req) =>
{
    var db = LoadDb();
    string cleanEmail = (req.Email ?? "").Trim().ToLower();
    string today = GetTodayKey();

    var customer = db.Customers.FirstOrDefault(x =>
        !string.IsNullOrWhiteSpace(x.Email) &&
        x.Email.Trim().ToLower() == cleanEmail &&
        x.Password == req.Password);

    if (customer != null)
    {
        if (customer.LastVisitDate == today)
            customer.TodayVisits++;
        else
        {
            customer.LastVisitDate = today;
            customer.TodayVisits = 1;
        }
        SaveDb(db);
        return Results.Ok(new { success = true, customer });
    }

    return Results.Unauthorized();
});

app.Run();

// ==========================================
// 4. كلاسات قاعدة البيانات (Models)
// ==========================================
public record LoginReq(string Password);
public record CustomerLoginReq(string Email, string Password);
public record VisitTrackReq(string? Email);

public class CustomerAccount
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Company { get; set; } = "";
    public string Password { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string LastVisitDate { get; set; } = "";
    public int TodayVisits { get; set; } = 1;
}

public class AppDatabase
{
    public string AdminPassword { get; set; } = "2005";
    public List<JsonElement> Fabrics { get; set; } = new();
    public List<JsonElement> Machines { get; set; } = new();
    public Dictionary<string, JsonElement> Batches { get; set; } = new();
    public List<JsonElement> Orders { get; set; } = new();
    public List<CustomerAccount> Customers { get; set; } = new();
    public Dictionary<string, int> DailyVisits { get; set; } = new();
}