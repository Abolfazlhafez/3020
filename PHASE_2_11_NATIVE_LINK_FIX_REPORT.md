# فاز 2.11 — رفع خطای لینک HEV در windows-build.yml

## خطای واقعی (از لاگ اجرای GitHub Actions، exit code 2)
```
ld: cannot find -lyaml
ld: cannot find -llwip
ld: cannot find -lhev-task-system
make: *** [Makefile:133: bin/libhev-socks5-tunnel.so] Error 1
```
کامپایل همه‌ی فایل‌ها موفق بود؛ فقط مرحله‌ی لینک شکست خورد.

## علت
`make shared` کتابخانه‌های third-party را به شکل `bin/lib*.so` می‌سازد. لینکر GNU ld در MSYS/Cygwin
برای `-lNAME` فقط `libNAME.dll.a`، `libNAME.a`، `msys-NAME.dll` یا `libNAME.dll` را جست‌وجو می‌کند و
فایل `.so` را نمی‌بیند.

## اصلاح (فقط `.github/workflows/windows-build.yml`)
1. `make tp-static` ← ساخت `libyaml.a`، `liblwip.a`، `libhev-task-system.a` (مثل بیلد ویندوزی خود upstream).
2. بررسی وجود سه آرشیو قبل از لینک.
3. `make -o tp-shared shared` ← لینک DLL نهایی با کتابخانه‌های استاتیک، بدون ساخت دوباره‌ی `.so`ها.
4. حلقه‌ی کپی کتابخانه‌های third-party حذف شد (دیگر چیزی برای بسته‌بندی نیست) و `wintun.dll` داخل درخت HEV
   عمداً کپی نمی‌شود؛ فقط Wintun رسمی و امضاشده در مرحله‌ی بعد اضافه می‌شود.
5. مسیر System32 به `cygpath -S` تغییر کرد (وابسته به درایو C: نباشد).

## تأییدشده
- YAML فایل workflow معتبر است (پارس شد).
- روی لینوکس: `make tp-static` با exit 0 سه `.a` ساخت و `make -o tp-shared shared` دوباره‌ی `.so` نساخت
  و `-lyaml/-llwip/-lhev-task-system` را به `.a` وصل کرد (این فقط مکانیزم make را ثابت می‌کند).

## تأییدنشده
- لینک واقعی روی MSYS2 در GitHub Actions اجرا نشده؛ باید لاگ اجرای بعدی بررسی شود.
- خطای `relocation ... recompile with -fPIC` در تست لینوکس مخصوص ELF بود و انتظار نمی‌رود روی PE رخ دهد، اما تأیید نشده.
- بارگذاری DLL وابسته به `msys-2.0.dll` داخل پروسه‌ی .NET هنوز تست نشده (ریسک شناخته‌شده؛ جایگزین: MinGW-w64).
- مراحل بعدی workflow (Wintun، dotnet build/test/publish، Inno Setup) هنوز به آن‌ها نرسیده‌ایم.

## گام بعد
workflow را دوباره اجرا کن و لاگ کامل را بفرست؛ خطاهای بعدی (در صورت وجود) از مرحله‌ی PE/exports یا dotnet خواهند بود.

---

# به‌روزرسانی بعد از اجرای دوم workflow (لاگ run #1، 2m39s)

## نتیجه‌ی واقعی لاگ
- **HEV native: موفق.** `libhev-socks5-tunnel.so` لینک شد، هر دو export
  (`hev_socks5_tunnel_main_from_file`, `hev_socks5_tunnel_quit`) در `objdump -p` دیده شد،
  `hev-socks5-tunnel.dll` و `msys-2.0.dll` با SHA-256 ثبت شدند. (ثبت‌شده در لاگ؛ بارگذاری در .NET هنوز تست نشده.)
- Wintun و `dotnet restore`: بدون خطا.
- **`dotnet build`: شکست، 27 خطا، همه CS0246** (`Stream`, `MemoryStream`, `SeekOrigin` پیدا نشد) در
  `WindowsHttp2ConnectSession.cs`، `WindowsSocks5ProxyServer.cs`، `WindowsSocks5UdpDnsRelay.cs`.

## علت
SDK ویندوز برای پروژه‌های `UseWPF=true` در `ImplicitUsings` فضای‌نام‌های `System.IO` و `System.Net.Http`
را حذف می‌کند. فایل‌های Http از قبل `using System.Net.Http;` صریح داشتند، اما `using System.IO;` در بسیاری از فایل‌ها نبود.
(وقتی خطاهای تعریف‌نوع وجود دارند، خطاهای داخل بدنه‌ی متدها مثل `File.`/`Path.` هنوز گزارش نشده‌اند؛
اصلاح زیر آن‌ها را هم پوشش می‌دهد.)

## اصلاح
`using System.IO;` صریح به 26 فایل `.cs` اضافه شد (پروژه‌ی اصلی و تست‌ها) که از انواع `System.IO` استفاده می‌کردند.
عمداً `using` سراسری در csproj نگذاشتم تا با `System.Windows.Shapes.Path` در کد WPF ابهام ایجاد نشود.

## تأییدنشده
- `dotnet build` اجرا نشده (در محیط من dotnet/NuGet در دسترس نیست). ممکن است بعد از رفع این دسته خطاهای دیگری آشکار شوند.
- `dotnet test`، publish، Inno Setup، و بارگذاری DLL مبتنی بر MSYS در .NET هنوز اجرا نشده‌اند.

---

# به‌روزرسانی سوم (اجرای بعدی workflow)

## نتیجه‌ی لاگ
خطاهای CS0246 از بین رفتند؛ `dotnet build` حالا فقط **یک** خطا دارد:
```
WindowsHttp2ConnectSession.cs(1137,40): error CS0508:
H2InputStream.ReadAsync(byte[], int, int, CancellationToken): return type must be 'Task<int>'
```

## علت و اصلاح
override مربوط به `Stream.ReadAsync(byte[], int, int, CancellationToken)` باید `Task<int>` برگرداند، نه `ValueTask<int>`.
اصلاح: `... => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();`
بقیه‌ی overrideهای `Stream` در پروژه (H2OutputStream، SplitDuplexStream، ScriptedStream) را بررسی کردم و امضایشان با `Stream` سازگار است.

## تأییدنشده
- `dotnet build` بعد از این اصلاح اجرا نشده؛ چون این خطا از نوع تعریف است، ممکن است خطاهای داخل بدنه‌ی متدها بعد از آن ظاهر شوند.
- `dotnet test`، publish، Inno Setup و بارگذاری DLL مبتنی بر MSYS در .NET هنوز اجرا نشده‌اند.
