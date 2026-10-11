FoxyVPN را از فاز 2.12 ادامه بده. Android حفظ شود؛ ویندوز: C#/.NET 8/WPF.
وضعیت (طبق لاگ‌های واقعی Actions): ساخت HEV native موفق شد (static third-party + DLL با exportهای لازم)؛
`dotnet build` به‌خاطر نبودن `using System.IO;` (WPF ImplicitUsings آن را حذف می‌کند) شکست خورد و اصلاح نوشته شده اما اجرا نشده.
گام‌ها:
1. لاگ اجرای بعدی `windows-build.yml` را از کاربر بگیر؛ خطاهای build/test را اصلاح کن.
2. اگر `msys-2.0.dll`-مبنا در .NET لود نشد، MinGW-w64 را جایگزین کن.
3. سپس tray، آمار ترافیک، ExitCheckEnabled، بازطراحی UI.
4. بدون لاگ و exit code هیچ ادعای «سبز» نکن؛ بخش «تأییدنشده» جدا بماند.
