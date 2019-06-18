#tool nuget:?package=GitVersion.CommandLine&version=4.0.0-beta0012
#tool nuget:?package=OctopusTools&version=6.7.0

#addin nuget:?package=Cake.Npm&version=0.17.0
#addin nuget:?package=Cake.Curl&version=4.1.0

#load build/paths.cake 
#load build/version.cake
#load build/package.cake
#load build/urls.cake

//#r myLib.dll

var target = Argument("Target", "Build");

Setup<PackageMetadata>(context =>
{
    return new PackageMetadata(
        outputDirectory: Argument("packageOutputDirectory", "packages"),
        name: "Linker-12");
});

Task("Compile")
    .Does(() =>
{
    DotNetCoreBuild(Paths.SolutionFile.FullPath);
});

Task("Test")
    .IsDependentOn("Compile")
    .Does(() =>
{
    DotNetCoreTest(Paths.SolutionFile.FullPath);
});

Task("Hello")
    .IsDependentOn("omg")
    .Does(() =>
{
    Information("Hello");

    Context.Log.Information("World");
});

Task("omg")
    .Does(() =>
{
    Information("omg hi!!");
});

Task("Version")
    .Does<PackageMetadata>(package =>
{
    package.Version = ReadVersionFromProjectFile(Context);

    if(package.Version == null)
    {
        Information("Project version missing, falling back to GitVersion");
        package.Version = GitVersion().FullSemVer;
    }

    Information($"Determined version number {package.Version}");
});

Task("Build-Frontend")
    .Does(() =>
{
    NpmInstall(settings => settings.FromPath(Paths.FrontendDirectory));

    NpmRunScript("build", settings => settings.FromPath(Paths.FrontendDirectory));
});

Task("Package-Zip")
    .IsDependentOn("Test")
    .IsDependentOn("Build-Frontend")
    .IsDependentOn("Version")
    .Does<PackageMetadata>(package =>
{
    CleanDirectory(package.OutputDirectory);
    
    package.Extension = "zip";

    DotNetCorePublish(
        Paths.WebProjectFile.GetDirectory().FullPath,
        new DotNetCorePublishSettings
        {
            OutputDirectory = Paths.PublishDirectory,
            NoBuild = true,
            NoRestore = true, 
            MSBuildSettings = new DotNetCoreMSBuildSettings
            {
                NoLogo = true
           }
        });

    Zip(Paths.PublishDirectory,package.FullPath);
});

Task("Package-Octopus")
    .IsDependentOn("Test")
    .IsDependentOn("Build-Frontend")
    .IsDependentOn("Version")
    .Does<PackageMetadata>(package =>
{
    CleanDirectory(package.OutputDirectory);
    
    package.Extension = "nupkg";

    DotNetCorePublish(
        Paths.WebProjectFile.GetDirectory().FullPath,
        new DotNetCorePublishSettings
        {
            OutputDirectory = Paths.PublishDirectory,
            NoBuild = true,
            NoRestore = true, 
            MSBuildSettings = new DotNetCoreMSBuildSettings
            {
                NoLogo = true
           }
        });

    OctoPack(
        package.Name,
        new OctopusPackSettings{
            Format = OctopusPackFormat.NuPkg,
            Version = package.Version,
            BasePath = Paths.PublishDirectory,
            OutFolder = package.OutputDirectory
        });
});

Task("Deploy-Kudu")
    .Description("Deploys to Kudu using the Zip deployment feature")
    .IsDependentOn("Package-Zip")
    .Does<PackageMetadata>(package =>
{
    CurlUploadFile(
        package.FullPath,
        Urls.KuduDeployUrl,
        new CurlSettings
        {
            Username = EnvironmentVariable("DeploymentUser"),
            Password = EnvironmentVariable("DeploymentPassword"),
            RequestCommand = "POST",
            ArgumentCustomization = args => args.Append("--fail")
        });
});

Task("Deploy-Octopus")
    .IsDependentOn("Package-Octopus")
    .Does<PackageMetadata>(package =>
{
    OctoPush(
        Urls.OctopusDeployUrl.AbsoluteUri,
        "API-PDOHI1N2LXAFRL53DRUSTLPWTBW",
        package.FullPath,
        new OctopusPushSettings
        {
            EnableServiceMessages = true
        });

    OctoCreateRelease(
        "Linker-12",
        new CreateReleaseSettings
        {
            Server = Urls.OctopusDeployUrl.AbsoluteUri,
            ApiKey = "API-PDOHI1N2LXAFRL53DRUSTLPWTBW",
            ReleaseNumber = package.Version,
            DefaultPackageVersion = package.Version,
            DeployTo = "Test",
            IgnoreExisting = true,
            DeploymentProgress = true,
            WaitForDeployment = true
        });
});

RunTarget(target);