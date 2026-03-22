# Sportive API — دليل التشغيل

## المتطلبات
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- MySQL 8.x (محليًا أو مستضافًا مثل Hostinger)
- Visual Studio 2022 / VS Code + C# Dev Kit (اختياري)

---

## هيكل المشروع

```
Sportive.API/
├── Controllers/
│   ├── AuthController.cs
│   ├── ProductsController.cs
│   ├── CategoriesController.cs    ← يتضمن CustomersController (نفس الملف)
│   ├── OrdersController.cs          ← يتضمن CartController (نفس الملف)
│   ├── CouponsController.cs
│   ├── DashboardController.cs
│   ├── ImagesController.cs
│   └── PaymentController.cs
├── Models/
├── DTOs/
├── Services/
├── Interfaces/
├── Data/
├── Middleware/
├── Validators/
├── Migrations/
├── Program.cs
├── appsettings.json
└── appsettings.Development.json
```

---

## الإعداد

### 1) سلسلة الاتصال بـ MySQL
في `appsettings.json` أو `appsettings.Development.json` (أو [User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) للتطوير):

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=YOUR_HOST;Port=3306;Database=YOUR_DB;User=YOUR_USER;Password=YOUR_PASSWORD;"
}
```

**MySQL محليًا (Windows):** شغّل خدمة MySQL وتأكد أن المنفذ `3306` مفتوح. إن ظهر خطأ اتصال، جرّب إضافة `SslMode=None;AllowPublicKeyRetrieval=true` لسلسلة الاتصال (موجودة افتراضيًا في `appsettings.Development.json`).

**استضافة بعيدة (مثل Hostinger):** فعّل **Remote MySQL** وأضف `%` أو عنوان IP جهازك في «Access host». مثال لسلسلة الاتصال (استبدل كلمة المرور من لوحة الاستضافة):

`Server=srv1787.hstgr.io;Port=3306;Database=u282618987_sportiveApi;User=u282618987_sportive;Password=...;SslMode=Required;AllowPublicKeyRetrieval=true;`

إن فشل الاتصال بسبب الشهادة، جرّب `SslMode=Preferred` أو راجع توثيق Hostinger لإصدار TLS.

**متغير بيئة:** إن كان لديك `ConnectionStrings__DefaultConnection` فارغًا في النظام أو في `launchSettings.json`، سيُلغى الإعداد من الملفات ويظهر اتصال بقاعدة فارغة — احذف المتغير أو عيّن القيمة الصحيحة.

### 2) مفتاح JWT
يجب أن يكون `JWT:Secret` طويلًا وعشوائيًا (يفضّل 32 حرفًا على الأقل). لا ترفع الأسرار إلى مستودع عام — استخدم User Secrets أو متغيرات البيئة في الإنتاج.

### 3) Redis (اختياري)
إذا تركت `ConnectionStrings:Redis` فارغة، يُستخدم تخزين مؤقت في الذاكرة. لـ Redis:

```json
"ConnectionStrings": {
  "Redis": "localhost:6379"
}
```

### 4) Paymob (عند استخدام الدفع)
عبّئ القيم تحت المفتاح `Paymob` في الإعدادات عند تفعيل `PaymentController`.

### 5) قاعدة البيانات

```bash
dotnet ef database update
```

أو أنشئ migration جديدة بعد تعديل النماذج:

```bash
dotnet ef migrations add YourMigrationName
dotnet ef database update
```

### 6) التشغيل

```bash
dotnet run
```

في بيئة التطوير، واجهة Swagger تكون على الجذر (`/`).

---

## نقاط API (ملخص)

| المجال | أمثلة |
|--------|--------|
| Auth | `POST /api/auth/register`, `POST /api/auth/login` (يرجع `customerId` لدور Customer)، `GET /api/auth/customer-id` (مع Bearer)، `POST /api/auth/change-password` |
| Products | `GET /api/products`, `GET /api/products/{id}`, إلخ. |
| Categories / Customers | `CategoriesController` و `CustomersController` |
| Orders | `GET /api/orders`, `GET /api/orders/my`، `POST /api/orders` بدون `customerId` (يُستخدم عميل المستخدم من التوكن). للـ Admin: `POST /api/orders?customerId=N` |
| Cart | `GET /api/cart/{customerId}` … |
| Coupons, Dashboard, Images, Payment | حسب الـ controllers في المشروع |

التفاصيل الدقيقة للمسارات: افتح Swagger بعد تشغيل المشروع.

---

## الأدوار
| الدور | وصف مختصر |
|--------|-----------|
| `Admin` | إدارة كاملة تقريبًا |
| `Customer` | العميل |
| `Staff` | طاقم (حسب الصلاحيات في الـ controllers) |

### مستخدم أدمن افتراضي (يُنشأ عند الـ seed)
```
Email:    admin@sportive.com
Password: Admin@123456
```
غيّر كلمة المرور فورًا في أي بيئة غير محلية.

---

## ربط حساب المستخدم بملف العميل
عند **التسجيل** أو **تسجيل الدخول**، يُنشأ تلقائيًا سجل في `Customers` مرتبط بالحساب (للمستخدمين بدور `Customer`)، ويُعاد الحقل **`customerId`** في JSON. استخدم هذا الرقم في مسارات السلة: `/api/cart/{customerId}` — لا تفترض `1` إلا إن كان يطابق المستخدم.

مسار `GET /api/auth/customer-id` (مع التوكن) يعيد `{ "customerId": N }` إن احتجت الرقم دون إعادة تسجيل الدخول.

مسار `GET /api/orders/my` يبحث عن عميل بـ `AppUserId` أو البريد إن لم يُربط سابقًا.

---

## الخطوة التالية (واجهة أمامية)
يمكن ربط React (مثل Vite على المنفذ 5173) — سياسة CORS في `Program.cs` تسمح بـ `localhost:3000` و `localhost:5173` وقيمة `AllowedOrigins` من الإعدادات.
