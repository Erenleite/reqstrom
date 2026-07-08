# ⚡ ReqStorm — Performance Testing Platform

> Professional k6 load testing platform with a modern dark-mode dashboard.  
> **Stack:** .NET 8 Minimal API (backend) · Vanilla HTML/JS (frontend) · k6 (load engine)

---

## 📋 Gereksinimler

| Araç | Kurulum |
|------|---------|
| [.NET 8 SDK](https://dotnet.microsoft.com/download) | `winget install Microsoft.DotNet.SDK.8` |
| [k6](https://k6.io/docs/get-started/installation/) | `winget install k6` |
| Modern tarayıcı | Chrome / Edge / Firefox |

---

## 🚀 Hızlı Başlangıç

### 1. Backend'i başlatın

```powershell
cd K6LoadTestEngine\K6LoadTestEngine
dotnet run
```

Backend `http://localhost:5085` adresinde ayağa kalkar.

### 2. Frontend'i açın

```
frontend\index.html
```

Dosyayı çift tıklayarak tarayıcıda açın — kurulum gerektirmez.

> **İpucu:** `Ctrl + Enter` kısayolu ile testi hızlıca başlatabilirsiniz.

---

## 📁 Proje Yapısı

```
K6LoadTestEngine\
├── K6LoadTestEngine\                  ← .NET 8 Web API (backend)
│   ├── Program.cs                     ← Minimal API endpoint tanımları
│   ├── K6LoadTestEngine.csproj        ← Proje dosyası (net8.0)
│   ├── appsettings.json
│   ├── Models\
│   │   ├── TestConfig.cs              ← İstek modeli (multi-endpoint + legacy compat)
│   │   │   └── EndpointConfig         ← Tekil endpoint tanımı (URL, method, weight, headers, CSV)
│   │   └── TestResult.cs              ← Yanıt modeli + dinamik percentile + zaman serisi
│   │       ├── TimeSeriesPoint        ← Saniye bazlı VU / Avg / pN verileri
│   │       └── ThresholdResult        ← SLA PASS/FAIL sonuçları
│   ├── Services\
│   │   ├── K6ScriptGenerator.cs       ← Dinamik JS script üretici (dual-executor + weighted routing)
│   │   ├── K6ProcessRunner.cs         ← k6 process yöneticisi (async + cancellation)
│   │   └── K6ResultParser.cs          ← NDJSON sonuç parser + dinamik percentile hesabı
│   └── wwwroot\                       ← Opsiyonel static file sunucu
└── frontend\
    └── index.html                     ← Tek dosya UI (CSS + JS inline, ~80 KB)
```

---

## 🔌 API Endpoints

| Method | URL | Açıklama |
|--------|-----|----------|
| `GET`  | `/api/health` | Sağlık kontrolü — `{ status: "ok", timestamp }` döner |
| `POST` | `/api/run-test` | Yük testi başlatır, tamamlanınca sonuçları döner |

### POST `/api/run-test` — İstek Şeması (Multi-Endpoint)

```json
{
  "endpoints": [
    {
      "url": "https://api.example.com/users",
      "httpMethod": "GET",
      "weight": 70,
      "requestBody": null,
      "headers": { "Authorization": "Bearer token123" },
      "useDynamicData": false,
      "csvDataBase64": null
    },
    {
      "url": "https://api.example.com/orders",
      "httpMethod": "POST",
      "weight": 30,
      "requestBody": "{\"item\": \"test\"}",
      "headers": { "X-Custom": "value" },
      "useDynamicData": false,
      "csvDataBase64": null
    }
  ],
  "durationSeconds": 30,
  "vUs": 10,
  "maxVUsLimit": 100,
  "targetRps": 0,
  "rampUpSeconds": 5,
  "rampDownSeconds": 5,
  "pctValue": 95,
  "pctThresholdMs": 500,
  "maxErrorRatePercent": 1.0,
  "headers": ["Content-Type=application/json"],
  "useDynamicData": false,
  "csvDataBase64": null
}
```

#### Legacy Tek Endpoint Formatı (Geriye Dönük Uyumlu)

Eski format hâlâ desteklenir — backend otomatik olarak `Endpoints` listesine dönüştürür:

```json
{
  "url": "https://api.example.com/endpoint",
  "httpMethod": "GET",
  "durationSeconds": 30,
  "vUs": 10,
  "p95ThresholdMs": 500,
  "maxErrorRatePercent": 1.0
}
```

### Yanıt Şeması (TestResult)

```json
{
  "success": true,
  "pctLabel": "p95",
  "pctValue": 95,
  "pctActualMs": 441.2,
  "pctThresholdMs": 500,
  "pctPassed": true,
  "p95ActualMs": 441.2,
  "p95ThresholdMs": 500,
  "p95Passed": true,
  "errorRateActualPercent": 0.2,
  "errorRateThresholdPercent": 1.0,
  "errorRatePassed": true,
  "totalRequests": 1500,
  "successRequests": 1497,
  "minDurationMs": 12.3,
  "maxDurationMs": 1250.0,
  "avgDurationMs": 212.4,
  "medDurationMs": 195.0,
  "p90DurationMs": 380.0,
  "avgRps": 48.3,
  "thresholds": [
    { "name": "Response Time (p95)", "target": "< 500ms", "actual": "441.2ms", "passed": true },
    { "name": "Error Rate", "target": "< 1%", "actual": "0.20%", "passed": true }
  ],
  "timeSeries": [
    { "timeSeconds": 0, "p95Ms": 120.0, "avgMs": 80.5, "activeVUs": 5, "rps": 12 }
  ],
  "terminalLogs": "..."
}
```

---

## ⚙️ Executor Modları

Script Generator, `targetRps` değerine göre **iki farklı k6 executor** seçer:

| Mod | Koşul | k6 Executor | Açıklama |
|-----|-------|-------------|----------|
| **Ramping VUs** | `targetRps = 0` | `ramping-vus` | VU sayısı yükü belirler; her VU sırayla istek atar |
| **Arrival Rate** | `targetRps > 0` | `ramping-arrival-rate` | Saniyede sabit istek sayısı hedeflenir; k6 VU havuzunu otomatik yönetir |

**Max VUs Limiti:** Arrival-rate modunda `maxVUsLimit` alanı, k6'nın gerektiğinde spawn edebileceği maksimum VU sayısını belirler. Tanımlanmamışsa `preAllocatedVUs × 5` kullanılır.

---

## 🌐 Çoklu Endpoint (Multi-Endpoint) Desteği

Tek testte birden fazla endpoint'i farklı trafik ağırlıklarıyla test edebilirsiniz.

### Nasıl Çalışır?

1. **Endpoint Listesi:** Her endpoint ayrı URL, HTTP method, request body, header ve CSV tanımına sahiptir.
2. **Ağırlıklı Dağılım:** Her endpoint'e bir `weight` (ağırlık) değeri atanır. Her k6 iterasyonunda `pickEndpoint()` fonksiyonu ağırlığa göre rastgele bir endpoint seçer.
3. **Trafik Paylaşımı:** `[70, 30]` weight değerleri → trafiğin %70'i ilk endpoint'e, %30'u ikinciye yönlendirilir.

```
Örnek: 3 endpoint, ağırlıklar [50, 30, 20]

  GET  /api/users     →  %50 trafik
  POST /api/orders    →  %30 trafik
  GET  /api/products  →  %20 trafik
```

### Endpoint Bazında Özellikler

| Özellik | Açıklama |
|---------|----------|
| **Per-endpoint URL & Method** | Her endpoint farklı URL ve HTTP method kullanabilir |
| **Per-endpoint Headers** | Endpoint'e özel header'lar (global header'larla merge edilir) |
| **Per-endpoint Request Body** | Her endpoint'e ayrı JSON body |
| **Per-endpoint CSV/Dinamik Veri** | Endpoint bazında Base64 CSV veya rastgele ID desteği |
| **Per-endpoint cURL Import** | Her endpoint satırında ayrı cURL yapıştırma alanı |

---

## 📊 Dinamik Percentile Threshold

SLA kontrolü artık sadece p95 ile sınırlı değil — istediğiniz percentile değerini seçebilirsiniz:

| Alan | Varsayılan | Açıklama |
|------|-----------|----------|
| `pctValue` | `95` | Kontrol edilecek percentile (1–99 arası). Örn: 50, 75, 80, 90, 95, 99 |
| `pctThresholdMs` | `500` | Seçilen percentile için ms cinsinden SLA eşiği |

Frontend'de hazır seçenekler sunulur:

```
p50 (Median) · p75 · p80 · p90 · p95 · p99 · Custom (1-99)
```

> **Geriye Dönük Uyumluluk:** Eski `p95ThresholdMs` alanı hâlâ desteklenir — backend tarafında `pctThresholdMs`'e otomatik dönüştürülür.

---

## ✨ Özellikler

### 🖥 Frontend (ReqStorm UI)

| Özellik | Detay |
|---------|-------|
| **Çoklu Endpoint** | Birden fazla endpoint'i ağırlık bazlı trafik dağılımıyla tek testte çalıştırma |
| **Per-endpoint cURL Import** | Her endpoint satırında curl komutunu yapıştır, form otomatik dolar (URL, method, header, body) |
| **Per-endpoint Headers** | Endpoint bazında dinamik header ekleme/silme |
| **Per-endpoint CSV/Dinamik Veri** | Endpoint bazında Base64 CSV upload veya rastgele ID desteği |
| **Weight Visualization** | Ağırlık toplam çubuğu ile trafik dağılımını görsel takip |
| **Executor Mode Badge** | RPS girildiğinde arrival-rate / VU-based modunu anlık gösterir |
| **Dual Executor Desteği** | `Target RPS` alanı ile ramping-arrival-rate modu etkinleştirilir |
| **Max VUs Kontrolü** | Arrival-rate testlerinde maksimum VU havuzu sınırı |
| **Dinamik Percentile** | p50, p75, p80, p90, p95, p99 veya custom percentile seçimi |
| **SLA Thresholds** | Seçilen percentile yanıt süresi ve Error Rate için PASS/FAIL kartları |
| **Performans Grafiği** | Canvas API ile VU sayısı, Avg, seçilen percentile zaman serisi çizimi |
| **Renklendirilmiş Terminal** | INFO (mavi) / WARN (sarı) / ERROR (kırmızı) / ENGINE (mor) renk kodlama |
| **Countdown Timer** | Test çalışırken tahmini süre geri sayımı |
| **Responsive Tasarım** | Mobil uyumlu (≤900px tek kolon) |

### ⚙️ Backend (K6LoadTestEngine)

| Özellik | Detay |
|---------|-------|
| **Minimal API** | .NET 8 Minimal API — düşük overhead |
| **Çoklu Endpoint** | `List<EndpointConfig>` ile birden fazla endpoint desteği |
| **Ağırlıklı Trafik Yönlendirme** | k6 scriptinde `pickEndpoint()` ile weighted random routing |
| **Dinamik Script Üretimi** | `K6ScriptGenerator` her test için geçici `.js` dosyası oluşturur |
| **Dinamik Percentile** | p50–p99 arası kullanıcının seçtiği percentile'ı hesaplar |
| **NDJSON Parser** | `K6ResultParser`, k6'nın `--out json` çıktısını satır satır okur |
| **Percentile Hesabı** | p50, p90 ve kullanıcı seçimli pN interpolasyon ile hesaplanır |
| **Zaman Serisi** | Saniye bazlı bucket'lara bölünmüş VU / Avg / pN verileri |
| **Process Cancellation** | `CancellationToken` ile test iptal edilebilir, `Kill()` ile process sonlandırılır |
| **60 dk Timeout** | Uzun testler için Kestrel timeout'ları genişletilmiş |
| **CORS** | Tüm origin'lere açık (yerel geliştirme) |
| **Per-endpoint CSV** | Endpoint bazında Base64 CSV decode edilerek temp dosyaya yazar |
| **Geriye Dönük Uyumluluk** | Eski tek-URL formatı ve `p95ThresholdMs` alanı desteklenir |

---

## 🎨 Tasarım Sistemi

Tek dosya (`index.html`) içinde tanımlı CSS custom property tabanlı design token'lar:

```css
--bg-base:        #0b0e17   /* Ana arka plan */
--accent-primary: #4ade80   /* Yeşil vurgu (PASS / logo) */
--accent-blue:    #60a5fa   /* Bilgi / istatistik rengi */
--accent-red:     #f87171   /* Hata / FAIL rengi */
--font-mono:      'JetBrains Mono'  /* Terminal & metrik fontları */
```

---

## ⌨️ Klavye Kısayolları

| Kısayol | Eylem |
|---------|-------|
| `Ctrl + Enter` | Testi başlat |

---

## 🗺 Mimari Akışı

```
Tarayıcı (frontend/index.html)
    │  POST /api/run-test (JSON — endpoints[] + config)
    ▼
.NET 8 Minimal API (Program.cs)
    ├─► Normalise()            →  Legacy format dönüşümü + pctValue clamp
    ├─► K6ScriptGenerator      →  /tmp/k6-load-engine/test_<timestamp>.js
    │     └─ pickEndpoint()    →  Ağırlıklı rastgele endpoint seçimi
    ├─► K6ProcessRunner        →  k6 run --out json=result.json <script>
    │       stdout/stderr      ←  k6 process
    └─► K6ResultParser         →  result.json (NDJSON) → TestResult
    │     └─ Percentile()      →  Dinamik pN hesabı (interpolasyon)
    ▼  HTTP 200 (TestResult JSON)
Tarayıcı — Threshold kartları, grafik, terminal logları render edilir
```

---

## 🛠 Geliştirme Notları

- Backend `wwwroot/` klasörü varsa `UseStaticFiles()` ile statik dosya sunabilir.
- Temp dosyalar (`%TEMP%\k6-load-engine\`) test sonrası otomatik temizlenir.
- Son üretilen script `last_generated_debug.js` olarak debug amaçlı saklanır.
- Arrival-rate modunda `sleep(1)` kaldırılır; k6 pacing'i kendisi yönetir.
- cURL parser'ı multi-line (`\` ile devam eden) komutları destekler ve endpoint bazında çalışır.
- Endpoint ağırlıkları 1'den küçük olamaz — `Normalise()` sırasında clamp edilir.
- `pctValue` 1–99 arasına clamp edilir.

---

## 📄 Lisans

MIT — Özgürce kullanın, fork edin, geliştirin.
