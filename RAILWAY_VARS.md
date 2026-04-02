# دليل إعداد متغيرات البيئة على Railway
# ============================================================
# ✅ استخدم هذا الدليل لإعداد كل الـ Secrets بشكل آمن
# لا ترفع أي من هذه القيم الحقيقية إلى GitHub أبداً
# ============================================================

## طريقة الإعداد على Railway
1. اذهب إلى مشروعك على Railway
2. اضغط على Service → Variables
3. أضف كل متغير من الجدول أدناه

## متغيرات البيئة المطلوبة
# ─────────────────────────────────────────────────────────────

### قاعدة البيانات
`DATABASE_URL` = Server=YOUR_HOST;Port=3306;Database=YOUR_DB;User=YOUR_USER;Password=YOUR_PASS;SslMode=Required;AllowPublicKeyRetrieval=true;Allow User Variables=true;

### JWT
`JWT__Secret` = YOUR_RANDOM_64_CHAR_SECRET_HERE
`JWT__Issuer` = Sportive.API
`JWT__Audience` = Sportive.Client
`JWT__ExpiresHours` = 72

### Cloudinary
`Cloudinary__CloudName` = YOUR_CLOUD_NAME
`Cloudinary__ApiKey` = YOUR_API_KEY
`Cloudinary__ApiSecret` = YOUR_API_SECRET

### Paymob
`Paymob__ApiKey` = YOUR_PAYMOB_API_KEY
`Paymob__IntegrationId` = YOUR_INTEGRATION_ID
`Paymob__HmacSecret` = YOUR_HMAC_SECRET

### Email (Gmail App Password — ليس كلمة السر العادية)
`Email__Host` = smtp.gmail.com
`Email__Port` = 587
`Email__User` = your-email@gmail.com
`Email__Pass` = YOUR_GMAIL_APP_PASSWORD
`Email__From` = your-email@gmail.com

### Backup Email
`Backup__Email__SmtpUser` = your-email@gmail.com
`Backup__Email__SmtpPass` = YOUR_GMAIL_APP_PASSWORD
`Backup__Email__To` = recipient@gmail.com
`Backup__Email__Enabled` = true

### Admin credentials (أول تشغيل فقط)
`ADMIN_EMAIL` = admin@your-domain.com
`ADMIN_PASSWORD` = Your_Secure_Admin_Password_Here

### CORS
`AllowedOrigins` = https://your-frontend-domain.com,https://admin.your-domain.com

### WhatsApp (اختياري)
`WhatsApp__PhoneNumberId` = YOUR_PHONE_NUMBER_ID
`WhatsApp__AccessToken` = YOUR_ACCESS_TOKEN

# ─────────────────────────────────────────────────────────────
## ملاحظات مهمة:
1. Railway يستخدم `__` (double underscore) للـ nested config
   * مثلاً: `JWT:Secret` في appsettings = `JWT__Secret` في Railway
2. لإنشاء Gmail App Password:
   * Google Account → Security → 2-Step Verification → App Passwords
3. لتوليد JWT Secret عشوائي آمن:
   * `node -e "console.log(require('crypto').randomBytes(64).toString('base64'))"`
# ─────────────────────────────────────────────────────────────
