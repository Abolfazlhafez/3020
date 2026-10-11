# گزارش ممیزی — فاز 2.7 (فقط CI / بسته‌بندی؛ بدون تغییر در کد Android و C#)

## ایرادهای پیدا‌شده و اصلاح‌شده

| # | شدت | فایل | ایراد (از روی سورس) | اصلاح |
|---|---|---|---|---|
| 1 | بالا | `.github/workflows/windows-build.yml` | `libhev-socks5-tunnel.so` فقط تغییر نام می‌داد و حلقه‌ی `ldd` همه‌ی وابستگی‌ها را کپی می‌کرد؛ این شامل DLLهای سیستمی `C:\Windows\System32` هم می‌شد | بررسی واقعی فرمت با `objdump -f` (باید `pei-x86-64` باشد)، بررسی export های C-API، کپی فقط وابستگی‌های MSYS از `/usr/bin`، پذیرفتن DLL سیستمی بدون کپی، شکست در import حل‌نشده، ثبت commit و SHA-256 |
| 2 | متوسط | همان | `gcc` / `objdump` صریحاً نصب نمی‌شد | `gcc binutils` اضافه شد؛ clone با `--shallow-submodules`؛ `timeout-minutes` و `permissions: contents: read` |
| 3 | بالا | `.github/workflows/android-ci.yml` + `app/build.gradle.kts` | اگر secretهای امضا نبود، Gradle بی‌صدا با کلید **debug** امضا می‌کرد ولی workflow می‌گفت «unsigned» و آن را در GitHub Release منتشر می‌کرد | job ریلیز بدون هر ۴ secret شکست می‌خورد؛ بعد از build با `apksigner verify --print-certs` رد می‌شود اگر گواهی «Android Debug» باشد؛ `fail_on_unmatched_files` |
| 4 | کم | `android-ci.yml` | آپلود APK بدون `if-no-files-found: error` | اضافه شد |

## تست‌های اجراشده (واقعی)

| آزمایش | فرمان | نتیجه |
|---|---|---|
| اعتبار YAML سه workflow | `yaml.safe_load` | exit 0 |
| سینتکس bash سه اسکریپت استخراج‌شده | `bash -n` | exit 0 |
| گارد امضا بدون secret | اجرای اسکریپت با secret خالی | exit **1** (انتظار می‌رفت) |
| گارد امضا با secret ساختگی | اجرای اسکریپت با مقادیر dummy | exit 0 |

## Not Run / Blocked
- `dotnet restore/build/test`: **Blocked** — `api.nuget.org` در سندباکس قابل دسترسی نیست (NU1301).
- ساخت HEV با MSYS2، objdump روی خروجی واقعی، Wintun، publish، Inno Setup: **Not Run** (فقط روی runner ویندوز GitHub).
- ساخت و تست Android (Gradle/NDK): **Not Run**.
- هر تست تونل واقعی (احراز هویت، DNS/IP leak، rollback مسیر، reboot): **Blocked** (نیاز به ویندوز واقعی، Administrator و سرور VPN).
- اینکه DLL ساخته‌شده در MSYS (وابسته به `msys-2.0.dll`) داخل پروسه‌ی .NET بدرستی لود می‌شود یا نه: **تأییدنشده**. اگر در CI/اجرا مشکل داشت، مسیر جایگزین ساخت با MinGW-w64 لازم است.

## انجام‌نشده
- بازطراحی UI/UX اندروید و ویندوز و انیمیشن‌ها (بخش جدید پرامپت): **شروع نشده**؛ بدون امکان بیلد/اجرا نمی‌توانم درستی‌اش را تأیید کنم.
- بررسی عمیق منطق DoH/HTTP2/DNS و مدیریت race در اتصال/قطع: انجام نشده.
