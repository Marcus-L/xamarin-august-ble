# August Smart Lock Plugin for Xamarin Android

Uses Bluetooth LE (via the [Plugin.BLE](https://github.com/xabre/xamarin-bluetooth-le) plugin) to communicate with and manipulate an August Smart Lock. This is a rough port of the [node augustcl library](https://github.com/ethitter/augustctl) from @ethitter.

_This Plugin tested for Android Only!_ It might work on iOS, I haven't tried it but if you have an iOS device and it works, let me know.

## Installation

```powershell
Install-Package Plugin.Android.AugustLock
```

Add these permissions to AndroidManifest.xml (for Bluetooth LE). For Marshmallow and above, please follow [Requesting Runtime Permissions in Android Marshmallow](https://blog.xamarin.com/requesting-runtime-permissions-in-android-marshmallow/) and don't forget to prompt the user for the location permission.

```xml
<uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />
<uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
<uses-permission android:name="android.permission.BLUETOOTH" />
<uses-permission android:name="android.permission.BLUETOOTH_ADMIN" />
```

## Obtaining Offline Keys

The API operates using offline keys so that no access to the internet by the calling device or the lock is required. To obtain the keys, you must extract them from the August App. This used to be quite easy (in a debug mode in the app), but August has made it more difficult recently. In the future it may not be possible to extract the keys from the app, so do it now!

Key Extraction Instuctions: [Link](https://github.com/ethitter/augustctl#configuration)

## Usage

```csharp
private static async Task LockOrUnlock(string uuid, string key, int keyOffset)
{
    var august = new AugustLockDevice(uuid, key, keyOffset);
    if (await august.Connect())
    {
        try
        {
            await august.Toggle();
        }
        finally
        {
            await august.Disconnect();
        }
    }
    else
    {
        // out of range, timeout or other error...
    }
}

// example invocation
LockOrUnlock("a9fa1c721c1d", "eced208840ba35332c868d291c4162fb", 1);
```

If you want to see the debug output, hook up to the Debug EventHandler:

```csharp
var august = new AugustLockDevice(...);
august.DebugMessage += (src, msg) => Android.Util.Log.Debug("August", msg);
```

## License

[MIT](https://github.com/Marcus-L/xamarin-august-ble/blob/master/LICENSE)
