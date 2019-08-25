# August Smart Lock Plugin for Xamarin Android

Uses Bluetooth LE (via the [Plugin.BLE](https://github.com/xabre/xamarin-bluetooth-le) plugin) to communicate with and manipulate an August Smart Lock. This is a rough port of the [node augustcl library](https://github.com/ethitter/augustctl) from [@ethitter](https://github.com/ethitter).

_This Plugin was tested on Android Only!_ It might work on iOS, I haven't tried it but if you have an iOS device and it works, let me know.

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

### Rooted Android

1. Phone should be in [developer mode](https://www.howtogeek.com/129728/how-to-access-the-developer-options-menu-and-enable-usb-debugging-on-android-4.2/)
1. Have installed platform tools (adb). If you need them you can get the standalone tools [here](https://developer.android.com/studio/releases/platform-tools.html#download)
1. Phone is detected by adb when connected via USB. Test by running command `adb devices`, should show something like `FA6910301551 device` under "List of devices attached"
1. Dump the config file from the connected phone by running: `adb shell cat /data/data/com.august.luna/shared_prefs/PeripheralInfoCache.xml`
1. Take note of `bluetoothAddress` (this is the device ID), `handshakeKey` (aka the "offline key") and `handshakeKeyIndex` (aka the "offline key offset")

### Unrooted Android

**Update 12/1/17:** Looks like the app has now disabled backups via the "[android:allowBackup](https://developer.android.com/guide/topics/manifest/application-element.html#allowbackup)" manifest setting, so the procedure below no longer works. I'll leave it here for posterity (or if they re-enable backup in the future): 

1. Same steps 1-3 as in the Rooted phone instructions
1. Have linux tools available. An easy way to have them is to use Git Bash, part of Git. [Download Git](https://git-scm.com/downloads)
1. Back up the app files, run: `adb backup -f backup.ab -noapk com.august.luna`
1. Using Git Bash:
   1. `cd` to the directory of the `backup.ab` file
   1. Strip unnecessary data from the file, decompress it, unpack a specific file and parse it. Run: 
      ```shell
      dd if=backup.ab bs=24 skip=1 | openssl zlib -d | tar xv -O apps/com.august.luna/sp/PeripheralInfoCache.xml | grep -ohP  '&quot;[^&]+&quot;:(&quot;)?[^&]+(&quot;)?(?=[,}])'  | sed 's/&quot;/"/g'
      ```
      This should output the app preferences:
      ```shell
      "lockId":"5634B1E5A9DFD49C5897FBF53388C0FA"
      "bluetoothAddress":"CE:78:1E:AA:EB:94"
      "handshakeKey":"119FFA3E1AF914203303DBEDBC59F161"
      "handshakeKeyIndex":1
      "serialNumber":"L1GFSED420"
      "armFirmwareVersion":"1.0.192"
      "bluetoothFirmwareVersion":"1.1.20"
      "gitHashFirmwareVersion":"834c0380"
      "batteryLevel":"HIGH"
      "peripheralType":"Lock"
      ```
   1. Take note of `bluetoothAddress` (this is the device ID), `handshakeKey` (aka the "offline key") and `handshakeKeyIndex` (aka the "offline key offset")

### iOS

**Update:** Looks like this no longer works, I'll leave it here for posterity: 

The key and offset can be found in plist located at:

```
User Applications/August/Library/Preferences/com.august.iossapp.plist
```

This can be retrieved by using a file explorer like [iFunbox](http://www.i-funbox.com/en_download.html), and opening the plist in Xcode.

## Usage

```csharp
private static async Task LockOrUnlock(string id, string key, int keyOffset)
{
    var august = new AugustLockDevice(id, key, keyOffset);
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

// example invocation. NOTE: can pass in "0" for the ID if it is not known
LockOrUnlock("CE781EAAEB94", "119FFA3E1AF914203303DBEDBC59F161", 1);
```

If you want to see the debug output, hook up to the Debug EventHandler:

```csharp
var august = new AugustLockDevice(...);
august.DebugMessage += (src, msg) => Android.Util.Log.Debug("August", msg);
```

If you do not know the device's ID (the bluetooth address, strip out the ":" characters) but you do know the key and key offset, you can pass in "0" as the ID to the AugustLockDevice and it will try to use the key & offset provided on _any_ lock it finds. If you output the debug messages you should be able to see the device ID which you can use in the future to prevent connecting to the wrong lock if you have multiple locks in range. 

Depending on your phone and lock bluetooth reception (based what times out on the debug output), you may want to tweak the timeouts for scanning/connecting/etc:

```csharp
var august = new AugustLockDevice(...);
august.ScanTimeout = 7000; // timeouts are in milliseconds
august.ConnectTimeout = 7000;
august.CommandTimeout = 3000;
```

## License

[MIT](https://github.com/Marcus-L/xamarin-august-ble/blob/master/LICENSE)
