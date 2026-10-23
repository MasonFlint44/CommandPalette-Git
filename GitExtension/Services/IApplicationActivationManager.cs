// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace GitExtension.Services;

// Taken from PowerToys' Command Palette Windows Terminal extension
// (src/modules/cmdpal/ext/Microsoft.CmdPal.Ext.WindowsTerminal/Helpers/IApplicationActivationManager.cs).
[Flags]
internal enum ActivateOptions
{
    None = 0x00000000,
    DesignMode = 0x00000001,
    NoErrorUI = 0x00000002,
    NoSplashScreen = 0x00000004,
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
internal partial interface IApplicationActivationManager
{
    void ActivateApplication(string appUserModelId, string arguments, ActivateOptions options, out uint processId);

    void ActivateForFile(string appUserModelId, IntPtr /* IShellItemArray* */ itemArray, string verb, out uint processId);

    void ActivateForProtocol(string appUserModelId, IntPtr /* IShellItemArray* */ itemArray, out uint processId);
}
