using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddCors();

var app = builder.Build();
app.UseCors(x => x.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.Run();

// ========== DATA MODELS ==========
public class FileNode
{
    public string Name { get; set; }
    public string Path { get; set; }
    public bool IsDirectory { get; set; }
    public bool IsChecked { get; set; }
    public long Size { get; set; }
    public string FormattedSize { get; set; }
    public List<FileNode> Children { get; set; } = new List<FileNode>();
    public string Extension { get; set; }
}

public class ArchitectureRequest
{
    public string RootPath { get; set; }
    public int MaxDepth { get; set; } = 999;
}

public class GenerateRequest
{
    public List<string> SelectedPaths { get; set; }
    public string RootPath { get; set; }
}

public class SizeMode
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public List<string> ExcludePatterns { get; set; }
    public List<string> IncludeExtensions { get; set; }
    public bool IncludeAll { get; set; } = false;
}

// ========== API CONTROLLER ==========
[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private const string DefaultRootPath = @"D:\Программы\AI Agent\Site Agent";

    // Папки, которые всегда исключаются (кроме режима Full)
    private static readonly string[] AlwaysExclude = {
        ".git", "bin", "obj", ".vs", ".idea", ".vscode",
        "publish", ".github", "logs", "packages",
        "TestResults", "node_modules", "dist", "build", ".nuget"
    };

    // Режимы размера с правильной логикой
    private static readonly List<SizeMode> SizeModes = new List<SizeMode>
    {
        new SizeMode
        {
            Id = 1,
            Name = "Full",
            Description = "100% - АБСОЛЮТНО ВСЕ файлы",
            ExcludePatterns = new List<string>(),
            IncludeExtensions = new List<string>(), // Пустой означает ВСЕ
            IncludeAll = true // Флаг для включения всех файлов
        },
        new SizeMode
        {
            Id = 2,
            Name = "Large",
            Description = "~80% - код + конфиги (без тестов)",
            ExcludePatterns = new List<string> { "Test.cs", "Tests.cs", "Mock.cs", "_test.", ".test." },
            IncludeExtensions = new List<string> {
                ".cs", ".cshtml", ".razor", ".js", ".ts", ".jsx", ".tsx",
                ".html", ".css", ".scss", ".sass",
                ".csproj", ".sln", ".json", ".yml", ".yaml", ".xml", ".config",
                ".sql", ".md", ".txt",
                ".sh", ".cmd", ".bat", ".ps1",
                ".env", ".gitignore", ".dockerignore", ".editorconfig",
                "Dockerfile", "Makefile", "docker-compose"
            }
        },
        new SizeMode
        {
            Id = 3,
            Name = "Medium",
            Description = "~60% - backend + основные конфиги",
            ExcludePatterns = new List<string> {
                "Test.cs", "Tests.cs", "Mock.cs",
                "Migrations", "wwwroot", ".md", ".txt"
            },
            IncludeExtensions = new List<string> {
                ".cs", ".cshtml", ".razor",
                ".csproj", ".sln", ".json", ".config", ".xml",
                "Dockerfile", "docker-compose"
            }
        },
        new SizeMode
        {
            Id = 4,
            Name = "Small",
            Description = "~40% - только основной код",
            ExcludePatterns = new List<string> {
                "Test.cs", "Tests.cs", "Mock.cs",
                "Migrations", "wwwroot", "Properties", "Options"
            },
            IncludeExtensions = new List<string> {
                ".cs", ".csproj", ".json"
            }
        },
        new SizeMode
        {
            Id = 5,
            Name = "Tiny",
            Description = "~20% - минимальный код (Controllers + Models)",
            ExcludePatterns = new List<string> {
                "Test.cs", "Tests.cs", "Migrations", "Properties",
                "wwwroot", "Services", "Options", "Helpers", "Hubs",
                ".csproj", ".sln", ".json"
            },
            IncludeExtensions = new List<string> { ".cs" }
        }
    };

    [HttpGet("tree")]
    public IActionResult GetFileTree([FromQuery] string path = null, [FromQuery] int mode = 1)
    {
        var rootPath = string.IsNullOrEmpty(path) ? DefaultRootPath : path;
        if (!Directory.Exists(rootPath))
        {
            return BadRequest(new { error = $"Путь не существует: {rootPath}" });
        }

        var sizeMode = SizeModes.FirstOrDefault(m => m.Id == mode) ?? SizeModes[0];
        var tree = BuildFileTree(rootPath, rootPath, sizeMode, 0, 10);

        return Ok(new
        {
            tree = tree,
            rootPath = rootPath,
            mode = sizeMode,
            modes = SizeModes
        });
    }

    [HttpGet("modes")]
    public IActionResult GetModes()
    {
        return Ok(SizeModes);
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateRequest request)
    {
        if (request.SelectedPaths == null || !request.SelectedPaths.Any())
        {
            return BadRequest(new { error = "Не выбраны файлы" });
        }

        var sb = new StringBuilder();
        sb.AppendLine($"// Сгенерировано: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"// Выбрано файлов: {request.SelectedPaths.Count}");
        sb.AppendLine();

        long totalSize = 0;
        int processedCount = 0;

        foreach (var filePath in request.SelectedPaths.OrderBy(p => p))
        {
            if (!System.IO.File.Exists(filePath)) continue;

            try
            {
                var content = await System.IO.File.ReadAllTextAsync(filePath, Encoding.UTF8);
                var relativePath = Path.GetRelativePath(request.RootPath, filePath);
                var fileInfo = new FileInfo(filePath);

                sb.AppendLine($"==== {relativePath} ({FormatSize(fileInfo.Length)}) ====");
                sb.AppendLine(content);
                sb.AppendLine();

                totalSize += content.Length;
                processedCount++;
            }
            catch (Exception ex)
            {
                sb.AppendLine($"==== [ERROR: {filePath}] ====");
                sb.AppendLine($"// {ex.Message}");
                sb.AppendLine();
            }
        }

        var result = sb.ToString();
        var bytes = Encoding.UTF8.GetBytes(result);

        Response.Headers.Add("X-Total-Files", processedCount.ToString());
        Response.Headers.Add("X-Total-Size", FormatSize(totalSize));

        return File(bytes, "text/plain; charset=utf-8", $"generated_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
    }

    [HttpPost("architecture")]
    public IActionResult GenerateArchitecture([FromBody] ArchitectureRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.RootPath))
        {
            return BadRequest(new { error = "Не указан корневой путь" });
        }

        if (!Directory.Exists(request.RootPath))
        {
            return BadRequest(new { error = $"Путь не существует: {request.RootPath}" });
        }

        var maxDepth = request.MaxDepth > 0 ? request.MaxDepth : 999;

        var sb = new StringBuilder();
        sb.AppendLine("=== АРХИТЕКТУРА ПРОЕКТА ===");
        sb.AppendLine($"Корень: {request.RootPath}");
        sb.AppendLine($"Глубина: {(maxDepth >= 999 ? "Все уровни" : maxDepth.ToString())}");
        sb.AppendLine($"Сгенерировано: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        var rootName = Path.GetFileName(request.RootPath);
        if (string.IsNullOrEmpty(rootName))
            rootName = request.RootPath;

        sb.AppendLine($"📁 {rootName}");

        try
        {
            // Получаем содержимое корневой директории
            var rootDirs = Directory.GetDirectories(request.RootPath)
                .Where(d => !AlwaysExclude.Any(ex =>
                    Path.GetFileName(d).Equals(ex, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(d => Path.GetFileName(d))
                .ToArray();

            var rootFiles = Directory.GetFiles(request.RootPath)
                .OrderBy(f => Path.GetFileName(f))
                .ToArray();

            // Обрабатываем папки
            for (int i = 0; i < rootDirs.Length; i++)
            {
                var isLastItem = (i == rootDirs.Length - 1) && rootFiles.Length == 0;
                BuildDirectoryTree(sb, rootDirs[i], "", isLastItem, 1, maxDepth);
            }

            // Обрабатываем файлы в корне
            for (int i = 0; i < rootFiles.Length; i++)
            {
                var fileName = Path.GetFileName(rootFiles[i]);
                var isLastFile = (i == rootFiles.Length - 1);

                sb.Append(isLastFile ? "└─ 📄 " : "├─ 📄 ");
                sb.AppendLine(fileName);
            }

            if (rootDirs.Length == 0 && rootFiles.Length == 0)
            {
                sb.AppendLine("   [Папка пуста]");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[ОШИБКА]: {ex.Message}");
        }

        var resultText = sb.ToString();
        var bytes = Encoding.UTF8.GetBytes(resultText);

        return File(bytes, "text/plain; charset=utf-8",
            $"architecture_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
    }

    private void BuildDirectoryTree(StringBuilder sb, string dirPath, string indent,
        bool isLast, int currentDepth, int maxDepth)
    {
        if (currentDepth > maxDepth) return;

        var dirName = Path.GetFileName(dirPath);
        if (string.IsNullOrEmpty(dirName))
            dirName = dirPath;

        // Рисуем ветку и имя папки
        sb.Append(indent);
        sb.Append(isLast ? "└─ 📁 " : "├─ 📁 ");
        sb.AppendLine(dirName);

        // Формируем отступ для содержимого
        var newIndent = indent + (isLast ? "   " : "│  ");

        try
        {
            // Получаем содержимое папки
            var subDirs = Directory.GetDirectories(dirPath)
                .Where(d => !AlwaysExclude.Any(ex =>
                    Path.GetFileName(d).Equals(ex, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(d => Path.GetFileName(d))
                .ToArray();

            var subFiles = Directory.GetFiles(dirPath)
                .OrderBy(f => Path.GetFileName(f))
                .ToArray();

            // Рекурсивно обрабатываем подпапки
            for (int i = 0; i < subDirs.Length; i++)
            {
                var isLastDir = (i == subDirs.Length - 1) && subFiles.Length == 0;
                BuildDirectoryTree(sb, subDirs[i], newIndent, isLastDir,
                    currentDepth + 1, maxDepth);
            }

            // Выводим файлы БЕЗ размеров (как в вашем примере)
            for (int i = 0; i < subFiles.Length; i++)
            {
                var fileName = Path.GetFileName(subFiles[i]);
                var isLastFile = (i == subFiles.Length - 1);

                sb.Append(newIndent);
                sb.Append(isLastFile ? "└─ 📄 " : "├─ 📄 ");
                sb.AppendLine(fileName);
            }

            if (subDirs.Length == 0 && subFiles.Length == 0)
            {
                sb.Append(newIndent);
                sb.AppendLine("[Пусто]");
            }
        }
        catch (UnauthorizedAccessException)
        {
            sb.Append(newIndent);
            sb.AppendLine("[Доступ запрещен]");
        }
        catch (Exception ex)
        {
            sb.Append(newIndent);
            sb.AppendLine($"[Ошибка: {ex.Message}]");
        }
    }

    private FileNode BuildFileTree(string path, string rootPath, SizeMode mode, int depth, int maxDepth)
    {
        var name = path == rootPath ? Path.GetFileName(path) ?? "Root" : Path.GetFileName(path);
        var node = new FileNode
        {
            Name = name,
            Path = path,
            IsDirectory = Directory.Exists(path)
        };

        if (node.IsDirectory)
        {
            // Для режима Full не исключаем никакие папки
            if (mode.Id != 1 && AlwaysExclude.Any(ex => name.Equals(ex, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            if (depth < maxDepth)
            {
                try
                {
                    // Добавляем подпапки
                    foreach (var dir in Directory.GetDirectories(path))
                    {
                        var childNode = BuildFileTree(dir, rootPath, mode, depth + 1, maxDepth);
                        if (childNode != null)
                        {
                            node.Children.Add(childNode);
                        }
                    }

                    // Добавляем файлы
                    foreach (var file in Directory.GetFiles(path))
                    {
                        var fileName = Path.GetFileName(file);
                        var ext = Path.GetExtension(file).ToLower();

                        bool include = false;

                        // Если режим Full - включаем ВСЕ файлы
                        if (mode.IncludeAll)
                        {
                            include = true;
                        }
                        else
                        {
                            // Проверяем расширения
                            if (!string.IsNullOrEmpty(ext))
                            {
                                include = mode.IncludeExtensions.Contains(ext);
                            }

                            // Проверяем файлы без расширений
                            if (!include)
                            {
                                var fileNameLower = fileName.ToLower();
                                include = mode.IncludeExtensions.Any(inc =>
                                    fileNameLower.Equals(inc.ToLower()) ||
                                    fileNameLower.StartsWith(inc.ToLower()));
                            }
                        }

                        // Проверяем исключения (не для режима Full)
                        if (mode.Id != 1)
                        {
                            bool exclude = mode.ExcludePatterns.Any(pattern =>
                                fileName.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                            if (exclude) include = false;
                        }

                        if (include)
                        {
                            try
                            {
                                var fileInfo = new FileInfo(file);
                                var fileNode = new FileNode
                                {
                                    Name = fileName,
                                    Path = file,
                                    IsDirectory = false,
                                    Size = fileInfo.Length,
                                    FormattedSize = FormatSize(fileInfo.Length),
                                    Extension = ext,
                                    IsChecked = true
                                };
                                node.Children.Add(fileNode);
                            }
                            catch
                            {
                                // Игнорируем файлы, которые не можем прочитать
                            }
                        }
                    }

                    // Сортируем: сначала папки, потом файлы
                    node.Children = node.Children
                        .OrderByDescending(c => c.IsDirectory)
                        .ThenBy(c => c.Name)
                        .ToList();
                }
                catch (UnauthorizedAccessException)
                {
                    // Игнорируем папки без доступа
                }
            }

            // Вычисляем размер папки
            node.Size = CalculateFolderSize(node);
            node.FormattedSize = FormatSize(node.Size);
            node.IsChecked = node.Children.Any();
        }
        else
        {
            try
            {
                var fileInfo = new FileInfo(path);
                node.Size = fileInfo.Length;
                node.FormattedSize = FormatSize(fileInfo.Length);
                node.Extension = Path.GetExtension(path).ToLower();
                node.IsChecked = true;
            }
            catch
            {
                node.Size = 0;
                node.FormattedSize = "0 B";
            }
        }

        return node;
    }

    private long CalculateFolderSize(FileNode folder)
    {
        long size = 0;
        foreach (var child in folder.Children)
        {
            size += child.Size;
        }
        return size;
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}