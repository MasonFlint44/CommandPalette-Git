// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Nodes;
using GitExtension.Pages;
using Microsoft.CommandPalette.Extensions;

namespace GitExtension.Tests;

public class ScanPathsSetupFormTests : TestEnvironment
{
    private static string Payload(string scanPaths) =>
        new JsonObject { ["scanPaths"] = scanPaths }.ToJsonString();

    [Fact]
    public void Template_is_a_valid_card_with_the_scan_paths_input()
    {
        var card = JsonNode.Parse(new ScanPathsSetupForm(NewSettings()).TemplateJson)!;

        var input = card["body"]!.AsArray().Single(e => e!["type"]!.GetValue<string>() == "Input.Text")!;
        Assert.Equal("scanPaths", input["id"]!.GetValue<string>());
        Assert.True(input["isRequired"]!.GetValue<bool>());
    }

    [Fact]
    public void Saving_stores_the_paths_and_goes_home()
    {
        var settings = NewSettings();

        var result = new ScanPathsSetupForm(settings).SubmitForm(Payload(@"D:\Projects;\\wsl.localhost\Ubuntu\home\me\git"));

        Assert.Equal(CommandResultKind.GoHome, result.Kind);
        Assert.Equal(@"D:\Projects;\\wsl.localhost\Ubuntu\home\me\git", settings.ScanPaths);
        Assert.Equal(@"D:\Projects;\\wsl.localhost\Ubuntu\home\me\git", NewSettings().ScanPaths);
    }

    [Theory]
    [InlineData(@"C:\Users\me\""quoted"" folder")]
    [InlineData("C:\\tab\there")]
    [InlineData(@"C:\ünïcödé\repos")]
    public void Unusual_characters_survive_saving(string scanPaths)
    {
        var settings = NewSettings();

        new ScanPathsSetupForm(settings).SubmitForm(Payload(scanPaths));

        Assert.Equal(scanPaths, NewSettings().ScanPaths);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_paths_change_nothing(string scanPaths)
    {
        var settings = NewSettings();
        settings.UpdateScanPaths(@"D:\Projects");

        var result = new ScanPathsSetupForm(settings).SubmitForm(Payload(scanPaths));

        Assert.Equal(CommandResultKind.GoHome, result.Kind);
        Assert.Equal(@"D:\Projects", settings.ScanPaths);
    }

    [Fact]
    public void A_payload_without_the_input_changes_nothing()
    {
        var settings = NewSettings();
        settings.UpdateScanPaths(@"D:\Projects");

        new ScanPathsSetupForm(settings).SubmitForm("{}");

        Assert.Equal(@"D:\Projects", settings.ScanPaths);
    }

    [Fact]
    public void An_empty_payload_goes_back()
    {
        Assert.Equal(CommandResultKind.GoBack, new ScanPathsSetupForm(NewSettings()).SubmitForm("null").Kind);
    }
}
