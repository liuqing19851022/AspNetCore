// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.DotNet.OpenApi.Tests;
using Microsoft.DotNet.Tools;
using Microsoft.Extensions.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DotNet.OpenApi.Add.Tests
{
    public class OpenApiAddTests : OpenApiTestBase
    {
        public OpenApiAddTests(ITestOutputHelper output) : base(output){}

        [Fact]
        public async Task OpenApi_Add_ReuseItemGroup()
        {
            var project = CreateBasicProject(withSwagger: true);

            var app = new Program(_console, _tempDir.Root);
            var run = app.Run(new[] { "add", project.NSwagJsonFile });

            Assert.True(string.IsNullOrEmpty(_error.ToString()), $"Threw error: {_error.ToString()}");
            Assert.Equal(0, run);

            var secondRun = app.Run(new[] { "add", SwaggerJsonUrl});
            Assert.True(string.IsNullOrEmpty(_error.ToString()), $"Threw error: {_error.ToString()}");
            Assert.Equal(0, secondRun);

            var csproj = new FileInfo(project.Project.Path);
            string content;
            using (var csprojStream = csproj.OpenRead())
            using (var reader = new StreamReader(csprojStream))
            {
                content = await reader.ReadToEndAsync();
                Assert.Contains("<PackageReference Include=\"NSwag.MSBuild.CodeGeneration\" Version=\"", content);
                Assert.Contains($"<OpenApiReference Include=\"{project.NSwagJsonFile}\"", content);
            }
            var projXml = new XmlDocument();
            projXml.Load(csproj.FullName);

            var openApiRefs = projXml.GetElementsByTagName(Program.OpenApiReference);
            Assert.Same(openApiRefs[0].ParentNode, openApiRefs[1].ParentNode);
        }

        [Fact]
        public async Task OpenApi_Add_ProvideClassAndJsonName()
        {
            var expectedJsonName = "different.json";
            var className = "WhatAClass";

            var project = CreateBasicProject(withSwagger: false);

            var app = new Program(_console, _tempDir.Root);
            var run = app.Run(new[] { "add", SwaggerJsonUrl, "--class-name", className, "--output-file", expectedJsonName});

            Assert.True(string.IsNullOrEmpty(_error.ToString()), $"Threw error: {_error.ToString()}");
            Assert.Equal(0, run);

            var expectedJsonPath = Path.Combine(_tempDir.Root, expectedJsonName);

            // csproj contents
            using (var csprojStream = new FileInfo(project.Project.Path).OpenRead())
            using (var reader = new StreamReader(csprojStream))
            {
                var content = await reader.ReadToEndAsync();
                Assert.Contains("<PackageReference Include=\"NSwag.MSBuild.CodeGeneration\" Version=\"", content);
                Assert.Contains($"<OpenApiReference Include=\"{expectedJsonPath}\" ClassName=\"{className}\"", content);
            }

            Assert.True(File.Exists(expectedJsonPath));
            using (var jsonStream = new FileInfo(expectedJsonPath).OpenRead())
            using (var reader = new StreamReader(jsonStream))
            {
                var content = await reader.ReadToEndAsync();
                Assert.Contains("\"openapi\":", content);
            }
        }

        [Fact]
        public async Task OpenApi_Add_FromJson()
        {
            var project = CreateBasicProject(withSwagger: true);
            var nswagJsonFile = project.NSwagJsonFile;

            var app = new Program(_console, _tempDir.Root);
            var run = app.Run(new[] { "add", nswagJsonFile });

            Assert.True(string.IsNullOrEmpty(_error.ToString()), $"Threw error: {_error.ToString()}");
            Assert.Equal(0, run);

            // csproj contents
            var csproj = new FileInfo(project.Project.Path);
            using (var csprojStream = csproj.OpenRead())
            using (var reader = new StreamReader(csprojStream))
            {
                var content = await reader.ReadToEndAsync();
                Assert.Contains("<PackageReference Include=\"NSwag.MSBuild.CodeGeneration\" Version=\"", content);
                Assert.Contains($"<OpenApiReference Include=\"{nswagJsonFile}\"", content);
            }

            // Build project and make sure it compiles
            var buildProc = ProcessEx.Run(_outputHelper, _tempDir.Root, "dotnet", "build");
            await buildProc.Exited;
            Assert.True(buildProc.ExitCode == 0, $"Build failed: {buildProc.Output}");

            // Run project and make sure it doesn't crash
            using (var runProc = ProcessEx.Run(_outputHelper, _tempDir.Root, "dotnet", "run"))
            {
                Thread.Sleep(100);
                Assert.False(runProc.HasExited, $"Run failed with: {runProc.Output}");
            }
        }

        [Fact]
        public async Task OpenApi_UseProjectOption()
        {
            var project = CreateBasicProject(withSwagger: true);
            var nswagJsonFIle = project.NSwagJsonFile;

            var app = new Program(_console, _tempDir.Root);
            var run = app.Run(new[] { "--project", project.Project.Path, "add", nswagJsonFIle });

            Assert.True(string.IsNullOrEmpty(_error.ToString()), $"Threw error: {_error.ToString()}");
            Assert.Equal(0, run);

            // csproj contents
            var csproj = new FileInfo(project.Project.Path);
            using (var csprojStream = csproj.OpenRead())
            using (var reader = new StreamReader(csprojStream))
            {
                var content = await reader.ReadToEndAsync();
                Assert.Contains("<PackageReference Include=\"NSwag.MSBuild.CodeGeneration\" Version=\"", content);
                Assert.Contains($"<OpenApiReference Include=\"{nswagJsonFIle}\"", content);
            }
        }

        [Fact]
        public async Task OpenAPi_Add_FromCsProj()
        {
            var project = CreateBasicProject(withSwagger: false);

            using (var refProj = new TemporaryDirectory())
            {
                var refProjName = "refProj";
                refProj
                    .WithCSharpProject(refProjName)
                    .WithTargetFrameworks("netcoreapp3.0")
                    .Dir()
                    .Create();

                var app = new Program(_console, _tempDir.Root);
                var refProjFile = Path.Join(refProj.Root, $"{refProjName}.csproj");
                var run = app.Run(new[] { "add", refProjFile });

                Assert.True(string.IsNullOrEmpty(_error.ToString()), $"Threw error: {_error.ToString()}");
                Assert.Equal(0, run);

                // csproj contents
                using(var csprojStream = new FileInfo(project.Project.Path).OpenRead())
                using(var reader = new StreamReader(csprojStream))
                {
                    var content = await reader.ReadToEndAsync();
                    Assert.Contains("<PackageReference Include=\"NSwag.MSBuild.CodeGeneration\" Version=\"", content);
                    Assert.Contains($"<OpenApiProjectReference Include=\"{refProjFile}\"", content);
                }
            }
        }

        [Fact]
        public async Task OpenApi_Add_FromUrl()
        {
            var project = CreateBasicProject(withSwagger: false);

            var app = new Program(_console, _tempDir.Root);
            var run = app.Run(new[] { "add", SwaggerJsonUrl});

            Assert.True(string.IsNullOrEmpty(_error.ToString()), $"Threw error: {_error.ToString()}");
            Assert.Equal(0, run);

            var expectedJsonName = Path.Combine(_tempDir.Root, "swagger.v1.json");

            // csproj contents
            using (var csprojStream = new FileInfo(project.Project.Path).OpenRead())
            using (var reader = new StreamReader(csprojStream))
            {
                var content = await reader.ReadToEndAsync();
                Assert.Contains("<PackageReference Include=\"NSwag.MSBuild.CodeGeneration\" Version=\"", content);
                Assert.Contains($"<OpenApiReference Include=\"{expectedJsonName}\" ClassName=\"MyClient\" SourceUrl=\"{SwaggerJsonUrl}\"", content);
            }

            Assert.True(File.Exists(expectedJsonName));
            using (var jsonStream = new FileInfo(expectedJsonName).OpenRead())
            using (var reader = new StreamReader(jsonStream))
            {
                var content = await reader.ReadToEndAsync();
                Assert.Contains("\"openapi\":", content);
            }
        }

        [Fact]
        public async Task OpenApi_Add_MultipleTimes_OnlyOneReference()
        {
            var project = CreateBasicProject(withSwagger: true);
            var nswagJsonFile = project.NSwagJsonFile;

            var app = new Program(_console, _tempDir.Root);
            var run = app.Run(new[] { "add", nswagJsonFile });

            Assert.True(string.IsNullOrEmpty(_error.ToString()), $"Threw error: {_error.ToString()}");
            Assert.Equal(0, run);

            app = new Program(_console, _tempDir.Root);
            run = app.Run(new[] { "add", nswagJsonFile });

            Assert.True(string.IsNullOrEmpty(_error.ToString()), $"Threw error: {_error.ToString()}");
            Assert.Equal(0, run);

            // csproj contents
            var csproj = new FileInfo(project.Project.Path);
            using (var csprojStream = csproj.OpenRead())
            using (var reader = new StreamReader(csprojStream))
            {
                var content = await reader.ReadToEndAsync();
                var escapedPkgRef = Regex.Escape("<PackageReference Include=\"NSwag.MSBuild.CodeGeneration\" Version=\"");
                Assert.Single(Regex.Matches(content, escapedPkgRef));
                var escapedApiRef = Regex.Escape($"<OpenApiReference Include=\"{nswagJsonFile}\"");
                Assert.Single(Regex.Matches(content, escapedApiRef));
            }
        }
    }
}
