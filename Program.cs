using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using QRCoder;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors();

// 配置Kestrel最大请求体大小（1GB）
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 1024 * 1024 * 1024;
});

// 配置表单上传大小限制（1GB）
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 1024 * 1024 * 1024;
});

var app = builder.Build();
app.UseCors(x => x.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

var uploadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LanFileTransfer_uploads");
Directory.CreateDirectory(uploadsDir);

app.MapGet("/api/info", () =>
{
    var ip = GetLocalIPAddress();
    return Results.Json(new { ip, port = 8080, url = $"http://{ip}:8080" });
});

app.MapGet("/api/qrcode", (string? lang) =>
{
    var ip = GetLocalIPAddress();
    var url = $"http://{ip}:8080/?lang={lang ?? "zh"}";
    using var qrGenerator = new QRCodeGenerator();
    var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
    using var qrCode = new PngByteQRCode(qrCodeData);
    var qrBytes = qrCode.GetGraphic(10);
    var base64 = Convert.ToBase64String(qrBytes);
    return Results.Text($"data:image/png;base64,{base64}", "text/plain");
});

var validLangs = new[] { "zh", "en", "ru", "de", "fr", "es", "it" };
app.MapGet("/", (string? lang) => 
{
    var l = validLangs.Contains(lang) ? lang! : "zh";
    return Results.Text(GenerateHtml(l), "text/html");
});

app.MapPost("/upload", async (HttpRequest request) =>
{
    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file == null) return Results.BadRequest("No file");
    
    var filePath = Path.Combine(uploadsDir, file.FileName);
    using (var stream = File.Create(filePath))
    {
        await file.CopyToAsync(stream);
    }
    Console.WriteLine($"Uploaded: {file.FileName}");
    return Results.Ok(new { success = true, name = file.FileName });
});

app.MapGet("/api/files", () =>
{
    if (!Directory.Exists(uploadsDir)) return Results.Json(Array.Empty<object>());
    
    var files = Directory.GetFiles(uploadsDir).Select(f =>
    {
        var info = new FileInfo(f);
        return new { name = info.Name, size = FormatFileSize(info.Length), modified = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm") };
    });
    return Results.Json(files);
});

app.MapGet("/download/{name}", (string name) =>
{
    var filePath = Path.Combine(uploadsDir, name);
    if (!File.Exists(filePath)) return Results.NotFound();
    return Results.File(filePath, "application/octet-stream", name);
});

app.MapDelete("/delete/{name}", (string name) =>
{
    var filePath = Path.Combine(uploadsDir, name);
    if (File.Exists(filePath))
    {
        File.Delete(filePath);
        return Results.Json(new { success = true });
    }
    return Results.Json(new { success = false });
});

var ip = GetLocalIPAddress();
app.Run($"http://{ip}:8080");

var msg = $"局域网文件传输服务\n访问地址: http://{ip}:8080\n中文版: http://{ip}:8080/?lang=zh\nEnglish: http://{ip}:8080/?lang=en";
Console.WriteLine(msg);

var ipFile = Path.Combine(AppContext.BaseDirectory, "IP.txt");
File.WriteAllText(ipFile, msg);

static string GetLocalIPAddress()
{
    try
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
        socket.Connect("8.8.8.8", 65530);
        var endPoint = socket.LocalEndPoint as IPEndPoint;
        var ip = endPoint?.Address?.ToString();
        if (!string.IsNullOrEmpty(ip) && ip != "0.0.0.0")
            return ip;
    }
    catch { }
    
    try
    {
        var host = Dns.GetHostAddresses(Dns.GetHostName())
            .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        if (host != null && host.ToString() != "127.0.0.1")
            return host.ToString();
    }
    catch { }
    
    try
    {
        foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                continue;
            if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                continue;
            
            var props = ni.GetIPProperties();
            foreach (var addr in props.UnicastAddresses)
            {
                var ip = addr.Address.ToString();
                if (ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("172."))
                    return ip;
            }
        }
    }
    catch { }
    
    return "127.0.0.1";
}

static string FormatFileSize(long bytes)
{
    string[] sizes = ["B", "KB", "MB", "GB"];
    int order = 0;
    double size = bytes;
    while (size >= 1024 && order < sizes.Length - 1)
    {
        order++;
        size /= 1024;
    }
    return $"{size:0.##} {sizes[order]}";
}

static string GenerateHtml(string lang)
{
    try
    {
    var dict = new Dictionary<string, Dictionary<string, string>>
    {
        ["zh"] = new() { ["title"] = "局域网文件传输", ["subtitle"] = "扫描二维码或访问下方地址", ["uploadTitle"] = "上传文件", ["uploadTip"] = "点击下方选择文件或拖拽文件到这里", ["noFileSelected"] = "未选择任何文件", ["uploadedFiles"] = "已上传文件", ["noFiles"] = "暂无文件", ["download"] = "下载", ["delete"] = "删除", ["confirmDelete"] = "确定删除 ", ["uploading"] = "上传中 ", ["uploadSuccess"] = "上传成功!", ["uploadFailed"] = "上传失败: ", ["loading"] = "加载中...", ["selectFileText"] = "选择文件", ["helpTitle"] = "使用说明", ["helpStep1"] = "1. 扫描二维码或访问上方地址", ["helpStep2"] = "2. 选择文件后自动开始上传", ["helpStep3"] = "3. 上传完成后，文件显示在下方列表", ["helpStep4"] = "4. 点击下载按钮即可获取文件", ["helpNote"] = "注意：上传的文件保存在电脑的文档文件夹中" },
        ["en"] = new() { ["title"] = "LAN File Transfer", ["subtitle"] = "Scan QR code or visit the address below", ["uploadTitle"] = "Upload File", ["uploadTip"] = "Click to select a file or drag here", ["noFileSelected"] = "No file selected", ["uploadedFiles"] = "Uploaded Files", ["noFiles"] = "No files yet", ["download"] = "Download", ["delete"] = "Delete", ["confirmDelete"] = "Delete ", ["uploading"] = "Uploading ", ["uploadSuccess"] = "Upload successful!", ["uploadFailed"] = "Upload failed: ", ["loading"] = "Loading...", ["selectFileText"] = "Select File", ["helpTitle"] = "How to Use", ["helpStep1"] = "1. Scan QR code or visit the address above", ["helpStep2"] = "2. Select a file, upload starts automatically", ["helpStep3"] = "3. After uploading, file appears in the list below", ["helpStep4"] = "4. Click download button to get the file", ["helpNote"] = "Note: Uploaded files are saved in your computer's Documents folder" },
        ["ru"] = new() { ["title"] = "Локальная передача файлов", ["subtitle"] = "Отсканируйте QR-код или перейдите по адресу ниже", ["uploadTitle"] = "Загрузить файл", ["uploadTip"] = "Нажмите или перетащите файл сюда", ["noFileSelected"] = "Файл не выбран", ["uploadedFiles"] = "Загруженные файлы", ["noFiles"] = "Файлов пока нет", ["download"] = "Скачать", ["delete"] = "Удалить", ["confirmDelete"] = "Удалить ", ["uploading"] = "Загрузка ", ["uploadSuccess"] = "Загрузка успешна!", ["uploadFailed"] = "Ошибка загрузки: ", ["loading"] = "За加载ка...", ["selectFileText"] = "Выбрать файл", ["helpTitle"] = "Инструкция", ["helpStep1"] = "1. Отсканируйте QR-код или перейдите по адресу", ["helpStep2"] = "2. Выберите файл, загрузка начнется автоматически", ["helpStep3"] = "3. После загрузки файл появится в списке ниже", ["helpStep4"] = "4. Нажмите кнопку скачать для получения файла", ["helpNote"] = "Примечание: Файлы сохраняются в папке Документы" },
        ["de"] = new() { ["title"] = "LAN-Dateiübertragung", ["subtitle"] = "QR-Code scannen oder Adresse unten besuchen", ["uploadTitle"] = "Datei hochladen", ["uploadTip"] = "Klicken oder Datei hierher ziehen", ["noFileSelected"] = "Keine Datei ausgewählt", ["uploadedFiles"] = "Hochgeladene Dateien", ["noFiles"] = "Noch keine Dateien", ["download"] = "Herunterladen", ["delete"] = "Löschen", ["confirmDelete"] = "Löschen ", ["uploading"] = "Hochladen ", ["uploadSuccess"] = "Erfolgreich hochgeladen!", ["uploadFailed"] = "Fehler beim Hochladen: ", ["loading"] = "Laden...", ["selectFileText"] = "Datei wählen", ["helpTitle"] = "Anleitung", ["helpStep1"] = "1. QR-Code scannen oder Adresse oben besuchen", ["helpStep2"] = "2. Datei auswählen, Upload startet automatisch", ["helpStep3"] = "3. Nach dem Hochladen erscheint die Datei in der Liste", ["helpStep4"] = "4. Klicken Sie auf Herunterladen um die Datei zu erhalten", ["helpNote"] = "Hinweis: Dateien werden im Ordner Dokumente gespeichert" },
        ["fr"] = new() { ["title"] = "Transfert de fichiers LAN", ["subtitle"] = "Scanner le QR code ou visiter l'adresse ci-dessous", ["uploadTitle"] = "Téléverser un fichier", ["uploadTip"] = "Cliquer ou glisser un fichier ici", ["noFileSelected"] = "Aucun fichier sélectionné", ["uploadedFiles"] = "Fichiers téléversés", ["noFiles"] = "Aucun fichier", ["download"] = "Télécharger", ["delete"] = "Supprimer", ["confirmDelete"] = "Supprimer ", ["uploading"] = "Téléversement ", ["uploadSuccess"] = "Téléversement réussi!", ["uploadFailed"] = "Erreur de téléversement: ", ["loading"] = "Chargement...", ["selectFileText"] = "Sélectionner", ["helpTitle"] = "Mode d'emploi", ["helpStep1"] = "1. Scanner le QR code ou visiter l'adresse ci-dessus", ["helpStep2"] = "2. Sélectionner un fichier, le téléversement start automatiquement", ["helpStep3"] = "3. Après le téléversement, le fichier apparaît dans la liste", ["helpStep4"] = "4. Cliquer sur télécharger pour obtenir le fichier", ["helpNote"] = "Note: Les fichiers sont enregistrés dans le dossier Documents" },
        ["es"] = new() { ["title"] = "Transferencia de archivos LAN", ["subtitle"] = "Escanee el código QR o visite la dirección", ["uploadTitle"] = "Subir archivo", ["uploadTip"] = "Haga clic o arrastre el archivo aquí", ["noFileSelected"] = "Ningún archivo seleccionado", ["uploadedFiles"] = "Archivos subidos", ["noFiles"] = "Sin archivos", ["download"] = "Descargar", ["delete"] = "Eliminar", ["confirmDelete"] = "Eliminar ", ["uploading"] = "Subiendo ", ["uploadSuccess"] = "¡Subido con éxito!", ["uploadFailed"] = "Error al subir: ", ["loading"] = "Cargando...", ["selectFileText"] = "Seleccionar", ["helpTitle"] = "Instrucciones", ["helpStep1"] = "1. Escanee el código QR o visite la dirección de arriba", ["helpStep2"] = "2. Seleccione un archivo, la subida comienza automáticamente", ["helpStep3"] = "3. Después de subir, el archivo aparece en la lista", ["helpStep4"] = "4. Haga clic en descargar para obtener el archivo", ["helpNote"] = "Nota: Los archivos se guardan en la carpeta Documentos" },
        ["it"] = new() { ["title"] = "Transferimento file LAN", ["subtitle"] = "Scansiona il codice QR o visita l'indirizzo", ["uploadTitle"] = "Carica file", ["uploadTip"] = "Clicca o trascina il file qui", ["noFileSelected"] = "Nessun file selezionato", ["uploadedFiles"] = "File caricati", ["noFiles"] = "Nessun file", ["download"] = "Scarica", ["delete"] = "Elimina", ["confirmDelete"] = "Elimina ", ["uploading"] = "Caricamento ", ["uploadSuccess"] = "Caricamento riuscito!", ["uploadFailed"] = "Errore caricamento: ", ["loading"] = "Caricamento...", ["selectFileText"] = "Seleziona", ["helpTitle"] = "Istruzioni", ["helpStep1"] = "1. Scansiona il codice QR o visita l'indirizzo sopra", ["helpStep2"] = "2. Seleziona un file, il caricamento parte automaticamente", ["helpStep3"] = "3. Dopo il caricamento, il file appare nella lista", ["helpStep4"] = "4. Clicca su scarica per ottenere il file", ["helpNote"] = "Nota: I file vengono salvati nella cartella Documenti" }
    };
    
    var d = dict.ContainsKey(lang) ? dict[lang] : dict["en"];
    
    var ip = GetLocalIPAddress();
    var url = $"http://{ip}:8080/?lang={lang}";
    using var qrGenerator = new QRCodeGenerator();
    var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
    using var qrCode = new PngByteQRCode(qrCodeData);
    var qrBytes = qrCode.GetGraphic(10);
    var qrBase64 = Convert.ToBase64String(qrBytes);
    var qrDataUrl = $"data:image/png;base64,{qrBase64}";
    
    var langNames = new Dictionary<string, string>
    {
        ["zh"] = "中文", ["en"] = "English", ["ru"] = "Русский", ["de"] = "Deutsch",
        ["fr"] = "Français", ["es"] = "Español", ["it"] = "Italiano"
    };
    var langLinks = string.Join(" | ", dict.Keys.Select(k => k == lang ? $"<span>{langNames[k]}</span>" : $"<a href=\"/?lang={k}\">{langNames[k]}</a>"));
    
    var templatePath = Path.Combine(AppContext.BaseDirectory, "template.html");
    var html = File.ReadAllText(templatePath)
        .Replace("{{title}}", d["title"])
        .Replace("{{subtitle}}", d["subtitle"])
        .Replace("{{uploadTitle}}", d["uploadTitle"])
        .Replace("{{uploadTip}}", d["uploadTip"])
        .Replace("{{noFileSelected}}", d["noFileSelected"])
        .Replace("{{uploadedFiles}}", d["uploadedFiles"])
        .Replace("{{noFiles}}", d["noFiles"])
        .Replace("{{download}}", d["download"])
        .Replace("{{deleteBtn}}", d["delete"])
        .Replace("{{confirmDelete}}", d["confirmDelete"])
        .Replace("{{uploading}}", d["uploading"])
        .Replace("{{uploadSuccess}}", d["uploadSuccess"])
        .Replace("{{uploadFailed}}", d["uploadFailed"])
        .Replace("{{loading}}", d["loading"])
        .Replace("{{selectFileText}}", d["selectFileText"])
        .Replace("{{helpTitle}}", d["helpTitle"])
        .Replace("{{helpStep1}}", d["helpStep1"])
        .Replace("{{helpStep2}}", d["helpStep2"])
        .Replace("{{helpStep3}}", d["helpStep3"])
        .Replace("{{helpStep4}}", d["helpStep4"])
        .Replace("{{helpNote}}", d["helpNote"])
        .Replace("{{langLinks}}", langLinks)
        .Replace("{{lang}}", lang)
        .Replace("{{url}}", url)
         .Replace("{{qrcode}}", qrDataUrl);
        return html;
    }
    catch (Exception ex)
    {
        return $"<html><body><h1>Error</h1><p>{ex.Message}</p><pre>{ex.StackTrace}</pre></body></html>";
    }
}