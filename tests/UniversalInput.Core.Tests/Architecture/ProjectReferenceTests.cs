using System.Xml.Linq;

namespace UniversalInput.Core.Tests.Architecture;

/// <summary>
/// Правила зависимостей читаются прямо из файлов проектов: нарушение ломает тест, а не вскрывается позже.
/// Abstractions ни на что не ссылается; Core — только на Abstractions; backend — только на Abstractions и никогда на Core.
/// </summary>
public class ProjectReferenceTests
{
	private const string Prefix = "UniversalInput.";

	private static readonly string SourceDirectory = Path.Combine(FindRoot(), "src");

	private static string FindRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UniversalInput.slnx")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName
			?? throw new InvalidOperationException("Не найден UniversalInput.slnx выше " + AppContext.BaseDirectory);
	}

	private static IEnumerable<string> ProjectNames() =>
		Directory.GetDirectories(SourceDirectory).Select(d => Path.GetFileName(d)!);

	private static IReadOnlyList<string> ProjectReferences(string project) => Reference(project, "ProjectReference")
		.Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
		.ToList();

	private static IReadOnlyList<string> PackageReferences(string project) =>
		Reference(project, "PackageReference").ToList();

	private static IEnumerable<string> Reference(string project, string element)
	{
		var path = Path.Combine(SourceDirectory, project, project + ".csproj");
		return XDocument.Load(path)
			.Descendants(element)
			.Select(e => (string?)e.Attribute("Include") ?? string.Empty);
	}

	[Fact]
	public void AllExpectedProjectsAreFound()
	{
		var names = ProjectNames().ToList();

		Assert.Contains("UniversalInput.Abstractions", names);
		Assert.Contains("UniversalInput.Core", names);
		Assert.Contains("UniversalInput.Hosting", names);
		Assert.Contains("UniversalInput.Testing", names);
	}

	[Fact]
	public void Abstractions_ReferencesNothing()
	{
		Assert.Empty(ProjectReferences("UniversalInput.Abstractions"));
		Assert.Empty(PackageReferences("UniversalInput.Abstractions"));
	}

	[Fact]
	public void Core_ReferencesOnlyAbstractions_AndLoggingAbstractions()
	{
		Assert.Equal<string>(["UniversalInput.Abstractions"], ProjectReferences("UniversalInput.Core"));
		Assert.Equal<string>(["Microsoft.Extensions.Logging.Abstractions"], PackageReferences("UniversalInput.Core"));
	}

	[Fact]
	public void Hosting_ReferencesCoreAndAbstractionsOnly()
	{
		var references = ProjectReferences("UniversalInput.Hosting");

		Assert.Contains("UniversalInput.Core", references);
		string[] allowed = ["UniversalInput.Core", "UniversalInput.Abstractions"];
		Assert.All(references, r => Assert.Contains(r, allowed));
		Assert.DoesNotContain(PackageReferences("UniversalInput.Hosting"), p => p == "Microsoft.Extensions.Hosting");
	}

	[Fact]
	public void Backends_ReferenceOnlyAbstractions_NeverCore()
	{
		var backends = ProjectNames().Where(n => n.StartsWith(Prefix + "Backends.", StringComparison.Ordinal)).ToList();

		foreach (var backend in backends)
		{
			Assert.Equal<string>(["UniversalInput.Abstractions"], ProjectReferences(backend));
		}
	}

	[Fact]
	public void NoProjectOtherThanHostingAndTesting_ReferencesBackends()
	{
		foreach (var project in ProjectNames().Where(n => !n.StartsWith(Prefix + "Backends.", StringComparison.Ordinal)))
		{
			Assert.DoesNotContain(ProjectReferences(project), r => r.StartsWith(Prefix + "Backends.", StringComparison.Ordinal));
		}
	}

	[Fact]
	public void Testing_DoesNotReferenceHosting()
	{
		Assert.DoesNotContain("UniversalInput.Hosting", ProjectReferences("UniversalInput.Testing"));
	}
}
