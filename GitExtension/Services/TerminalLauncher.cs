// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GitExtension.Services;

/// <summary>
/// Launches Windows Terminal through Windows app activation, the way PowerToys' own
/// Windows Terminal extension does. Command Palette lends this extension foreground
/// rights before each command, and activation uses them, so the terminal opens in front
/// of the palette. Starting wt.exe as a plain process doesn't pass them on, so its
/// window could open behind.
/// </summary>
internal static partial class TerminalLauncher
{
    private const string WindowsTerminalAppId = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";
    private const uint ClsctxInprocServer = 0x1;

    private static readonly Guid ApplicationActivationManagerClsid = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");
    private static readonly Guid ApplicationActivationManagerIid = new("2e941141-7f97-4756-ba1d-9decde894a3d");
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    /// <summary>
    /// Opens Windows Terminal with the given wt.exe command line.
    /// </summary>
    public static void Launch(string arguments)
    {
        try
        {
            Activate(arguments);
        }
        catch (Exception)
        {
            // Not installed from the Store (e.g. an unpackaged install) or activation failed:
            // fall back to the wt.exe alias.
            ShellHelpers.OpenInShell("wt.exe", arguments);
        }
    }

    private static void Activate(string arguments)
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(
            in ApplicationActivationManagerClsid,
            IntPtr.Zero,
            ClsctxInprocServer,
            in ApplicationActivationManagerIid,
            out var pointer));

        try
        {
            var manager = (IApplicationActivationManager)ComWrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
            manager.ActivateApplication(WindowsTerminalAppId, arguments, ActivateOptions.None, out _);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, in Guid riid, out IntPtr ppv);
}
