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
    public int DetailLevel { get; set; } = 1; // 1-Full, 2-Standard, 3-Minimal
    public bool CompactMode { get; set; } = true; // Компактный режим - не показывать пустые папки
    public bool ShowEmptyIndicator { get; set; } = false; // Показывать ли (empty) для пустых папок
    public bool ShowFileSize { get; set; } = false; // Показывать размер файлов
}

public class GenerateRequest
{
    public List<string> SelectedPaths { get; set; }
    public string RootPath { get; set; }
    public bool ShowFileSize { get; set; } = false; // Добавляем опцию показа размера
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

public class UnityArchitectureLevel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public List<string> IncludeFolders { get; set; }
    public List<string> ExcludeFolders { get; set; }
    public List<string> IncludeExtensions { get; set; }
    public bool ShowAllFiles { get; set; } = false;
}

// ========== API CONTROLLER ==========
[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private const string DefaultRootPath = @"D:\Программы\AI Agent\Site Agent";

    // Папки, которые всегда исключаются для обычных проектов
    private static readonly string[] AlwaysExclude = {
        ".git", "bin", "obj", ".vs", ".idea", ".vscode",
        "publish", ".github", "logs", "packages",
        "TestResults", "node_modules", "dist", "build", ".nuget"
    };

    // Добавьте этот уровень 4 в список UnityArchitectureLevels в Program.cs
    // Замените существующий список или добавьте этот уровень

    private static readonly List<UnityArchitectureLevel> UnityArchitectureLevels = new List<UnityArchitectureLevel>
{
    new UnityArchitectureLevel
    {
        Id = 1,
        Name = "Full",
        Description = "Полная структура - все папки и файлы",
        IncludeFolders = new List<string>(),
        ExcludeFolders = new List<string>(),
        IncludeExtensions = new List<string>(),
        ShowAllFiles = true
    },
    new UnityArchitectureLevel
    {
        Id = 2,
        Name = "Standard",
        Description = "Стандартная структура - основные папки Unity",
        IncludeFolders = new List<string>
        {
            "Assets", "Packages", "ProjectSettings", "UserSettings"
        },
        ExcludeFolders = new List<string>
        {
            "Library", "Temp", "Logs", "MemoryCaptures", "Recordings",
            "obj", "Build", "Builds", ".vs", ".idea",
            "*.app", "*.exe", "*_Data", "*_BurstDebugInformation_DoNotShip"
        },
        IncludeExtensions = new List<string>
        {
            ".cs", ".shader", ".cginc", ".hlsl", ".compute",
            ".prefab", ".unity", ".mat", ".asset", ".controller",
            ".asmdef", ".asmref", ".json", ".xml", ".yaml",
            ".md", ".txt", ".pdf"
        },
        ShowAllFiles = false
    },
    new UnityArchitectureLevel
    {
        Id = 3,
        Name = "Minimal",
        Description = "Минимальная структура - только скрипты Unity",
        IncludeFolders = new List<string>
        {
            "Assets/Scripts", "Assets/Editor", "Assets/Plugins",
            "Assets/Resources", "Assets/StreamingAssets"
        },
        ExcludeFolders = new List<string>
        {
            "Library", "Temp", "Logs", "obj", "Build", "Builds",
            "UserSettings", "MemoryCaptures", "Recordings",
            ".vs", ".idea", "Packages", "ProjectSettings",
            "Assets/Textures", "Assets/Materials", "Assets/Models",
            "Assets/Animations", "Assets/Audio", "Assets/Fonts",
            "Assets/Sprites", "Assets/UI", "Assets/Prefabs"
        },
        IncludeExtensions = new List<string>
        {
            ".cs", ".asmdef", ".asmref"
        },
        ShowAllFiles = false
    },
    new UnityArchitectureLevel
    {
        Id = 4,
        Name = "CodeOnly",
        Description = "Только код - .cs, .csproj, .html, .js файлы",
        IncludeFolders = new List<string>(), // Не ограничиваем папки
        ExcludeFolders = new List<string>
        {
            ".git", "Library", "Temp", "Logs", "obj", "Build", "Builds",
            "UserSettings", "MemoryCaptures", "Recordings",
            ".vs", ".idea", "node_modules", "packages"
        },
        IncludeExtensions = new List<string>
        {
            ".cs", ".csproj", ".html", ".js", ".jsx", ".ts", ".tsx", ".css", ".scss"
        },
        ShowAllFiles = false
    }
};

    // Также нужно обновить метод ShouldIncludeFile, чтобы он правильно фильтровал файлы:
    private bool ShouldIncludeFile(string filePath, UnityArchitectureLevel level)
    {
        if (level == null || level.ShowAllFiles)
            return true;

        if (level.IncludeExtensions == null || !level.IncludeExtensions.Any())
            return true;

        var extension = Path.GetExtension(filePath).ToLower();
        var fileName = Path.GetFileName(filePath).ToLower();

        // Проверяем расширение файла
        return level.IncludeExtensions.Any(ext =>
            extension.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    // И метод ShouldExcludeDirectory тоже нужно обновить для корректной работы:
    private bool ShouldExcludeDirectory(string dirPath, List<string> excludePatterns,
        UnityArchitectureLevel level, string rootPath)
    {
        var dirName = Path.GetFileName(dirPath);

        // Сначала проверяем исключаемые папки
        if (excludePatterns != null && excludePatterns.Any())
        {
            foreach (var pattern in excludePatterns)
            {
                if (dirName.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                    dirPath.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // Если есть список включаемых папок, проверяем его
        if (level != null && level.IncludeFolders != null && level.IncludeFolders.Any())
        {
            var relativePath = Path.GetRelativePath(rootPath, dirPath).Replace('\\', '/');

            bool shouldInclude = level.IncludeFolders.Any(includeFolder =>
                relativePath.StartsWith(includeFolder, StringComparison.OrdinalIgnoreCase) ||
                includeFolder.StartsWith(relativePath, StringComparison.OrdinalIgnoreCase));

            return !shouldInclude;
        }

        return false;
    }

    // Добавьте новый режим "CodeOnly" в список SizeModes в Program.cs:

    private static readonly List<SizeMode> SizeModes = new List<SizeMode>
{
    new SizeMode
    {
        Id = 1,
        Name = "Full",
        Description = "100% - ВСЕ файлы",
        ExcludePatterns = new List<string>(),
        IncludeExtensions = new List<string>(),
        IncludeAll = true
    },
    new SizeMode
    {
        Id = 2,
        Name = "Large",
        Description = "~80% - код + конфиги",
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
        Description = "~60% - backend + конфиги",
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
        Description = "~40% - основной код",
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
        Description = "~20% - минимальный код",
        ExcludePatterns = new List<string> {
            "Test.cs", "Tests.cs", "Migrations", "Properties",
            "wwwroot", "Services", "Options", "Helpers", "Hubs",
            ".csproj", ".sln", ".json"
        },
        IncludeExtensions = new List<string> { ".cs" }
    },
    new SizeMode
    {
        Id = 6,
        Name = "CodeOnly",
        Description = "Только код - .cs, .html, .js, .csproj",
        ExcludePatterns = new List<string> {
            "Test.cs", "Tests.cs", "Mock.cs", ".sample"
        },
        IncludeExtensions = new List<string> {
            ".cs", ".csproj", ".html", ".js", ".jsx", ".ts", ".tsx", ".css"
        }
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

    [HttpGet("unity-architecture-levels")]
    public IActionResult GetUnityArchitectureLevels()
    {
        return Ok(UnityArchitectureLevels);
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
        sb.AppendLine($"// Показывать размер: {(request.ShowFileSize ? "Да" : "Нет")}");
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

                // Формируем заголовок с размером или без
                string header = request.ShowFileSize
                    ? $"==== {relativePath} ({FormatSize(fileInfo.Length)}) ===="
                    : $"==== {relativePath} ====";

                sb.AppendLine(header);
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

        // Добавляем итоговую статистику в конец файла
        if (request.ShowFileSize)
        {
            sb.AppendLine();
            sb.AppendLine("// ========== СТАТИСТИКА ==========");
            sb.AppendLine($"// Обработано файлов: {processedCount}");
            sb.AppendLine($"// Общий размер контента: {FormatSize(totalSize)}");
            sb.AppendLine($"// Средний размер файла: {(processedCount > 0 ? FormatSize(totalSize / processedCount) : "0 B")}");
        }

        var result = sb.ToString();
        var bytes = Encoding.UTF8.GetBytes(result);

        Response.Headers.Add("X-Total-Files", processedCount.ToString());
        Response.Headers.Add("X-Total-Size", FormatSize(totalSize));

        // Добавляем индикатор размера в имя файла
        string sizeIndicator = request.ShowFileSize ? "_with_sizes" : "";
        return File(bytes, "text/plain; charset=utf-8", $"generated{sizeIndicator}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
    }

    // В методе GenerateArchitecture в Program.cs обновите логику обработки уровней:

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
        bool isUnityProject = IsUnityProject(request.RootPath);

        UnityArchitectureLevel architectureLevel = null;
        List<string> includeExtensions = null;

        if (isUnityProject)
        {
            // Для Unity проектов используем UnityArchitectureLevels
            architectureLevel = UnityArchitectureLevels.FirstOrDefault(l => l.Id == request.DetailLevel)
                ?? UnityArchitectureLevels[0];
        }
        else
        {
            // Для обычных проектов используем упрощенную логику на основе DetailLevel
            switch (request.DetailLevel)
            {
                case 1: // Full
                    includeExtensions = null; // Показываем все
                    break;
                case 2: // Large
                    includeExtensions = new List<string> {
                    ".cs", ".cshtml", ".razor", ".js", ".ts", ".jsx", ".tsx",
                    ".html", ".css", ".scss", ".sass",
                    ".csproj", ".sln", ".json", ".yml", ".yaml", ".xml", ".config",
                    ".sql", ".md", ".txt", ".sh", ".cmd", ".bat", ".ps1",
                    ".env", ".gitignore", ".dockerignore", ".editorconfig",
                    "Dockerfile", "Makefile", "docker-compose"
                };
                    break;
                case 3: // Medium
                    includeExtensions = new List<string> {
                    ".cs", ".cshtml", ".razor",
                    ".csproj", ".sln", ".json", ".config", ".xml",
                    "Dockerfile", "docker-compose"
                };
                    break;
                case 4: // CodeOnly
                    includeExtensions = new List<string> {
                    ".cs", ".csproj", ".html", ".js", ".jsx", ".ts", ".tsx", ".css"
                };
                    break;
                default:
                    includeExtensions = null;
                    break;
            }

            // Создаем временный уровень для обычных проектов
            if (includeExtensions != null)
            {
                architectureLevel = new UnityArchitectureLevel
                {
                    Id = request.DetailLevel,
                    Name = $"Level{request.DetailLevel}",
                    Description = "Custom level for non-Unity project",
                    IncludeFolders = new List<string>(),
                    ExcludeFolders = AlwaysExclude.ToList(),
                    IncludeExtensions = includeExtensions,
                    ShowAllFiles = false
                };
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== АРХИТЕКТУРА ПРОЕКТА ===");
        sb.AppendLine($"Тип проекта: {(isUnityProject ? "Unity Project" : "General Project")}");
        sb.AppendLine($"Корень: {request.RootPath}");
        sb.AppendLine($"Глубина: {(maxDepth >= 999 ? "Все уровни" : maxDepth.ToString())}");

        if (architectureLevel != null)
        {
            sb.AppendLine($"Уровень детализации: {architectureLevel.Name} - {architectureLevel.Description}");
        }

        sb.AppendLine($"Режим отображения: {(request.CompactMode ? "Компактный (без пустых папок)" : "Полный")}");
        sb.AppendLine($"Сгенерировано: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        var rootName = Path.GetFileName(request.RootPath);
        if (string.IsNullOrEmpty(rootName))
            rootName = request.RootPath;

        sb.AppendLine($"📁 {rootName}");

        try
        {
            List<string> excludeFolders = architectureLevel?.ExcludeFolders ?? AlwaysExclude.ToList();

            var rootDirs = Directory.GetDirectories(request.RootPath)
                .Where(d => !ShouldExcludeDirectory(d, excludeFolders, architectureLevel, request.RootPath))
                .OrderBy(d => Path.GetFileName(d))
                .ToArray();

            var rootFiles = Directory.GetFiles(request.RootPath)
                .Where(f => ShouldIncludeFile(f, architectureLevel))
                .OrderBy(f => Path.GetFileName(f))
                .ToArray();

            // Фильтрация пустых папок в компактном режиме
            if (request.CompactMode)
            {
                var nonEmptyDirs = new List<string>();
                foreach (var dir in rootDirs)
                {
                    if (HasContentInDirectory(dir, excludeFolders, architectureLevel, request.RootPath, 0, maxDepth))
                    {
                        nonEmptyDirs.Add(dir);
                    }
                }
                rootDirs = nonEmptyDirs.ToArray();
            }

            for (int i = 0; i < rootDirs.Length; i++)
            {
                var isLastItem = (i == rootDirs.Length - 1) && rootFiles.Length == 0;
                BuildDirectoryTreeOptimized(sb, rootDirs[i], "", isLastItem, 1, maxDepth,
                    architectureLevel, request.RootPath, isUnityProject, request.CompactMode,
                    request.ShowEmptyIndicator, request.ShowFileSize);
            }

            for (int i = 0; i < rootFiles.Length; i++)
            {
                var fileName = Path.GetFileName(rootFiles[i]);
                var isLastFile = (i == rootFiles.Length - 1);

                sb.Append(isLastFile ? "└─ " : "├─ ");

                string fileIcon = isUnityProject
                    ? GetUnityFileIcon(Path.GetExtension(fileName).ToLower())
                    : "📄";

                string sizeInfo = "";
                if (request.ShowFileSize)
                {
                    try
                    {
                        var fileInfo = new FileInfo(rootFiles[i]);
                        sizeInfo = $" [{FormatSize(fileInfo.Length)}]";
                    }
                    catch
                    {
                        sizeInfo = " [?]";
                    }
                }

                sb.AppendLine($"{fileIcon} {fileName}{sizeInfo}");
            }

            if (rootDirs.Length == 0 && rootFiles.Length == 0)
            {
                sb.AppendLine("   📭 [Папка не содержит файлов по выбранным критериям]");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[ОШИБКА]: {ex.Message}");
        }

        var resultText = sb.ToString();
        var bytes = Encoding.UTF8.GetBytes(resultText);

        return File(bytes, "text/plain; charset=utf-8",
            $"architecture_{(isUnityProject ? "unity_" : "")}level{request.DetailLevel}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
    }
    private void BuildDirectoryTreeOptimized(StringBuilder sb, string dirPath, string indent,
        bool isLast, int currentDepth, int maxDepth, UnityArchitectureLevel level,
        string rootPath, bool isUnityProject, bool compactMode, bool showEmptyIndicator, bool showFileSize = false)
    {
        if (currentDepth > maxDepth) return;

        var dirName = Path.GetFileName(dirPath);
        if (string.IsNullOrEmpty(dirName))
            dirName = dirPath;

        List<string> excludeFolders = level?.ExcludeFolders ?? new List<string>();

        var subDirs = Directory.GetDirectories(dirPath)
            .Where(d => !ShouldExcludeDirectory(d, excludeFolders, level, rootPath))
            .OrderBy(d => Path.GetFileName(d))
            .ToArray();

        var subFiles = Directory.GetFiles(dirPath)
            .Where(f => ShouldIncludeFile(f, level))
            .OrderBy(f => Path.GetFileName(f))
            .ToArray();

        // В компактном режиме фильтруем пустые подпапки
        if (compactMode)
        {
            var nonEmptyDirs = new List<string>();
            foreach (var dir in subDirs)
            {
                if (HasContentInDirectory(dir, excludeFolders, level, rootPath, currentDepth, maxDepth))
                {
                    nonEmptyDirs.Add(dir);
                }
            }
            subDirs = nonEmptyDirs.ToArray();
        }

        bool hasContent = subFiles.Length > 0 || subDirs.Length > 0;

        // В компактном режиме не показываем совсем пустые папки
        if (compactMode && !hasContent && currentDepth > 0)
        {
            return;
        }

        sb.Append(indent);
        sb.Append(isLast ? "└─ " : "├─ ");

        string folderIcon = isUnityProject ? GetUnityFolderIcon(dirName) : "📁";
        string emptyIndicator = "";

        if (showEmptyIndicator && !hasContent)
        {
            emptyIndicator = " 📭";
        }

        sb.AppendLine($"{folderIcon} {dirName}{emptyIndicator}");

        var newIndent = indent + (isLast ? "   " : "│  ");

        try
        {
            for (int i = 0; i < subDirs.Length; i++)
            {
                var isLastDir = (i == subDirs.Length - 1) && subFiles.Length == 0;
                BuildDirectoryTreeOptimized(sb, subDirs[i], newIndent, isLastDir,
                    currentDepth + 1, maxDepth, level, rootPath, isUnityProject, compactMode, showEmptyIndicator, showFileSize);
            }

            for (int i = 0; i < subFiles.Length; i++)
            {
                var fileName = Path.GetFileName(subFiles[i]);
                var isLastFile = (i == subFiles.Length - 1);

                sb.Append(newIndent);
                sb.Append(isLastFile ? "└─ " : "├─ ");

                string fileIcon = isUnityProject
                    ? GetUnityFileIcon(Path.GetExtension(fileName).ToLower())
                    : "📄";

                // Добавляем размер файла если нужно
                string sizeInfo = "";
                if (showFileSize)
                {
                    try
                    {
                        var fileInfo = new FileInfo(subFiles[i]);
                        sizeInfo = $" [{FormatSize(fileInfo.Length)}]";
                    }
                    catch
                    {
                        sizeInfo = " [?]";
                    }
                }

                sb.AppendLine($"{fileIcon} {fileName}{sizeInfo}");
            }

            // Показываем [Пусто] только если не в компактном режиме и папка действительно пустая
            if (!compactMode && !hasContent)
            {
                sb.Append(newIndent);
                sb.AppendLine("📭 [Нет файлов по критериям]");
            }
        }
        catch (UnauthorizedAccessException)
        {
            sb.Append(newIndent);
            sb.AppendLine("⚠️ [Доступ запрещен]");
        }
        catch (Exception ex)
        {
            sb.Append(newIndent);
            sb.AppendLine($"❌ [Ошибка: {ex.Message}]");
        }
    }

    private bool HasContentInDirectory(string dirPath, List<string> excludeFolders,
        UnityArchitectureLevel level, string rootPath, int currentDepth, int maxDepth)
    {
        if (currentDepth >= maxDepth) return false;

        try
        {
            // Проверяем файлы в текущей папке
            var hasFiles = Directory.GetFiles(dirPath)
                .Any(f => ShouldIncludeFile(f, level));

            if (hasFiles) return true;

            // Рекурсивно проверяем подпапки
            var subDirs = Directory.GetDirectories(dirPath)
                .Where(d => !ShouldExcludeDirectory(d, excludeFolders, level, rootPath))
                .ToArray();

            foreach (var dir in subDirs)
            {
                if (HasContentInDirectory(dir, excludeFolders, level, rootPath, currentDepth + 1, maxDepth))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Игнорируем ошибки доступа
            return false;
        }

        return false;
    }

    private bool IsUnityProject(string rootPath)
    {
        return Directory.Exists(Path.Combine(rootPath, "Assets")) ||
               Directory.Exists(Path.Combine(rootPath, "ProjectSettings")) ||
               Directory.Exists(Path.Combine(rootPath, "Library")) ||
               System.IO.File.Exists(Path.Combine(rootPath, "Assembly-CSharp.csproj"));
    }



    private void BuildDirectoryTree(StringBuilder sb, string dirPath, string indent,
        bool isLast, int currentDepth, int maxDepth, UnityArchitectureLevel level,
        string rootPath, bool isUnityProject)
    {
        if (currentDepth > maxDepth) return;

        var dirName = Path.GetFileName(dirPath);
        if (string.IsNullOrEmpty(dirName))
            dirName = dirPath;

        // Предварительно проверяем, есть ли что показывать в этой папке
        List<string> excludeFolders = level?.ExcludeFolders ?? new List<string>();

        var subDirs = Directory.GetDirectories(dirPath)
            .Where(d => !ShouldExcludeDirectory(d, excludeFolders, level, rootPath))
            .OrderBy(d => Path.GetFileName(d))
            .ToArray();

        var subFiles = Directory.GetFiles(dirPath)
            .Where(f => ShouldIncludeFile(f, level))
            .OrderBy(f => Path.GetFileName(f))
            .ToArray();

        // Проверяем, есть ли содержимое на любом уровне вложенности
        bool hasContent = subFiles.Length > 0 || HasContentInSubdirectories(subDirs, excludeFolders, level, rootPath, currentDepth, maxDepth);

        // Не показываем папку, если она полностью пустая (включая подпапки)
        if (!hasContent && currentDepth > 0) // Корневую папку показываем всегда
        {
            return;
        }

        sb.Append(indent);
        sb.Append(isLast ? "└─ " : "├─ ");

        // Добавляем индикатор пустой папки прямо к имени
        string folderIcon = isUnityProject ? GetUnityFolderIcon(dirName) : "📁";
        string emptyIndicator = !hasContent ? " (empty)" : "";
        sb.AppendLine($"{folderIcon} {dirName}{emptyIndicator}");

        // Если папка пустая, не обрабатываем дальше
        if (!hasContent)
        {
            return;
        }

        var newIndent = indent + (isLast ? "   " : "│  ");

        try
        {
            // Обрабатываем подпапки
            for (int i = 0; i < subDirs.Length; i++)
            {
                var isLastDir = (i == subDirs.Length - 1) && subFiles.Length == 0;
                BuildDirectoryTree(sb, subDirs[i], newIndent, isLastDir,
                    currentDepth + 1, maxDepth, level, rootPath, isUnityProject);
            }

            // Обрабатываем файлы
            for (int i = 0; i < subFiles.Length; i++)
            {
                var fileName = Path.GetFileName(subFiles[i]);
                var isLastFile = (i == subFiles.Length - 1);

                sb.Append(newIndent);
                sb.Append(isLastFile ? "└─ " : "├─ ");

                if (isUnityProject)
                {
                    var ext = Path.GetExtension(fileName).ToLower();
                    sb.AppendLine($"{GetUnityFileIcon(ext)} {fileName}");
                }
                else
                {
                    sb.AppendLine($"📄 {fileName}");
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            sb.Append(newIndent);
            sb.AppendLine("⚠️ [Доступ запрещен]");
        }
        catch (Exception ex)
        {
            sb.Append(newIndent);
            sb.AppendLine($"❌ [Ошибка: {ex.Message}]");
        }
    }

    // Новый вспомогательный метод для проверки содержимого в подпапках
    private bool HasContentInSubdirectories(string[] directories, List<string> excludeFolders,
        UnityArchitectureLevel level, string rootPath, int currentDepth, int maxDepth)
    {
        if (currentDepth >= maxDepth) return false;

        foreach (var dir in directories)
        {
            try
            {
                // Проверяем файлы в текущей подпапке
                var files = Directory.GetFiles(dir)
                    .Where(f => ShouldIncludeFile(f, level))
                    .Any();

                if (files) return true;

                // Рекурсивно проверяем подпапки
                var subDirs = Directory.GetDirectories(dir)
                    .Where(d => !ShouldExcludeDirectory(d, excludeFolders, level, rootPath))
                    .ToArray();

                if (HasContentInSubdirectories(subDirs, excludeFolders, level, rootPath, currentDepth + 1, maxDepth))
                {
                    return true;
                }
            }
            catch
            {
                // Игнорируем ошибки доступа при проверке
                continue;
            }
        }

        return false;
    }

    private string GetUnityFolderIcon(string folderName)
    {
        return folderName.ToLower() switch
        {
            "scripts" => "📝",
            "prefabs" => "🎭",
            "materials" => "🎨",
            "textures" => "🖼️",
            "editor" => "⚙️",
            "resources" => "📦",
            "plugins" => "🔌",
            "animations" => "🎬",
            "audio" => "🔊",
            "models" => "🎲",
            "shaders" => "✨",
            "sprites" => "🖼️",
            "ui" => "🖥️",
            "fonts" => "🔤",
            "scenes" => "🏞️",
            _ => "📁"
        };
    }

    private string GetUnityFileIcon(string extension)
    {
        return extension switch
        {
            ".cs" => "📜",
            ".prefab" => "🎭",
            ".unity" => "🏞️",
            ".mat" => "🎨",
            ".shader" or ".cginc" or ".hlsl" or ".compute" => "✨",
            ".asmdef" or ".asmref" => "📋",
            ".controller" => "🎮",
            ".asset" => "📦",
            ".png" or ".jpg" or ".jpeg" or ".tga" => "🖼️",
            ".fbx" or ".obj" or ".dae" => "🎲",
            ".anim" or ".animation" => "🎬",
            ".mp3" or ".wav" or ".ogg" => "🔊",
            ".ttf" or ".otf" => "🔤",
            ".json" => "📄",
            ".xml" or ".yaml" => "📋",
            ".md" or ".txt" => "📝",
            _ => "📄"
        };
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
            if (mode.Id != 1 && AlwaysExclude.Any(ex => name.Equals(ex, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            if (depth < maxDepth)
            {
                try
                {
                    foreach (var dir in Directory.GetDirectories(path))
                    {
                        var childNode = BuildFileTree(dir, rootPath, mode, depth + 1, maxDepth);
                        if (childNode != null)
                        {
                            node.Children.Add(childNode);
                        }
                    }

                    foreach (var file in Directory.GetFiles(path))
                    {
                        var fileName = Path.GetFileName(file);
                        var ext = Path.GetExtension(file).ToLower();

                        bool include = false;

                        if (mode.IncludeAll)
                        {
                            include = true;
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(ext))
                            {
                                include = mode.IncludeExtensions.Contains(ext);
                            }

                            if (!include)
                            {
                                var fileNameLower = fileName.ToLower();
                                include = mode.IncludeExtensions.Any(inc =>
                                    fileNameLower.Equals(inc.ToLower()) ||
                                    fileNameLower.StartsWith(inc.ToLower()));
                            }
                        }

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