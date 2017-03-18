using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.Android.AugustLock
{
    // ported from https://github.com/ethitter/augustctl/blob/master/lib/lock.js
    public class AugustLockDevice
    {
        private Guid AUGUST_SERVICE = new Guid("bd4ac610-0b45-11e3-8ffd-0800200c9a66");
        private LockSession lock_session;
        private SecureLockSession secure_lock_session;
        private bool isSecure;
        private IDevice augustLock;

        // public properties
        public Guid ID { get; private set; }
        public string OfflineKey { get; private set; }
        public int OfflineKeyOffset { get; private set; }
        public int ScanTimeout { get; set; }
        public int ConnectTimeout { get; set; }
        public int CommandTimeout { get; set; }

        // events
        public event EventHandler<string> DebugMessage;

        public AugustLockDevice(string id, string offlineKey, int offlineKeyOffset)
        {
            ScanTimeout = 7000;
            ConnectTimeout = 7000;
            CommandTimeout = 3000;

            if (id == "0")
            {
                id = "000000000000";
            }
            ID = new Guid("00000000-0000-0000-0000-" + id);

            OfflineKey = offlineKey;
            OfflineKeyOffset = offlineKeyOffset;
        }

        internal void Debug(string log)
        {
            DebugMessage?.Invoke(this, log);
        }

        public async Task<bool> Connect()
        {
            try
            {
                augustLock = null;
                var adapter = CrossBluetoothLE.Current.Adapter;
                var tokenSource = new CancellationTokenSource();
                EventHandler<DeviceEventArgs> lockDiscovered = (s, a) =>
                {
                    augustLock = a.Device;
                    tokenSource.Cancel();
                };
                try
                {
                    adapter.DeviceDiscovered += lockDiscovered;
                    var scanTask = adapter.StartScanningForDevicesAsync(
                        new[] { AUGUST_SERVICE }, device => device.Id == ID || ID.Equals(Guid.Empty), false, tokenSource.Token);

                    if (await Task.WhenAny(scanTask, Task.Delay(ScanTimeout)) == scanTask)
                    {
                        await scanTask;
                    }
                    else
                    {
                        Debug("scan timed out");
                    }
                }
                catch (Exception ex)
                {
                    Debug("scan exceptioned: " + ex.GetType().ToString() + ":" + ex.Message + "\n" + ex.StackTrace);
                }
                finally
                {
                    adapter.DeviceDiscovered -= lockDiscovered; // detach handler
                    try
                    {
                        await adapter.StopScanningForDevicesAsync();
                    }
                    catch (Exception iex)
                    {
                        Debug("Could not stop scanning for devices: " + iex.Message);
                    }
                }
                if (augustLock != null)
                {
                    var connectTask = adapter.ConnectToDeviceAsync(augustLock);
                    if (await Task.WhenAny(new[] { connectTask, Task.Delay(ConnectTimeout) }) == connectTask)
                    {
                        await connectTask; // in case the task exceptioned out to throw the exception
                        await SetupSessions(augustLock);
                        await PerformHandshake();
                        return true;
                    }
                    else
                    {
                        Debug("connect (after successful scan) timed out");
                    }
                }
                else
                {
                    Debug("august lock not found.");
                }
            }
            catch (Exception ex)
            {
                Debug("connect error: " + ex.Message);
            }
            return false;
        }

        private byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length / 2).Select(x => Convert.ToByte(hex.Substring(x * 2, 2), 16)).ToArray();
        }

        private async Task SetupSessions(IDevice peripheral)
        {
            Debug("connected to lock: " + peripheral.Name + ">" + peripheral.Id);
            var service = await peripheral.GetServiceAsync(AUGUST_SERVICE);

            secure_lock_session = new SecureLockSession(this)
            {
                CommandTimeout = CommandTimeout,
                OfflineKeyOffset = (byte)OfflineKeyOffset,
                ReadCharacteristic = await service.GetCharacteristicAsync(new Guid("bd4ac614-0b45-11e3-8ffd-0800200c9a66")),
                WriteCharacteristic = await service.GetCharacteristicAsync(new Guid("bd4ac613-0b45-11e3-8ffd-0800200c9a66"))
            };
            secure_lock_session.SetKey(StringToByteArray(OfflineKey));
            lock_session = new LockSession(this)
            {
                CommandTimeout = CommandTimeout,
                ReadCharacteristic = await service.GetCharacteristicAsync(new Guid("bd4ac612-0b45-11e3-8ffd-0800200c9a66")),
                WriteCharacteristic = await service.GetCharacteristicAsync(new Guid("bd4ac611-0b45-11e3-8ffd-0800200c9a66"))
            };

            // start up pipes
            await secure_lock_session.Start();
            await lock_session.Start();
        }

        private async Task PerformHandshake()
        {
            var handshakeKeys = new byte[16];
            RandomNumberGenerator.Create().GetBytes(handshakeKeys);

            var cmd = secure_lock_session.BuildCommand(0x01);
            Array.Copy(handshakeKeys, 0, cmd, 4, 8);
            var response = await secure_lock_session.Execute(cmd);
            if (response[0] != 0x02)
            {
                throw new Exception("unexpected response to SEC_LOCK_TO_MOBILE_KEY_EXCHANGE: " + BitConverter.ToString(response));
            }
            Debug("handshake complete");

            // secure session established
            isSecure = true;

            // setup the session key
            var sessionKey = new byte[16];
            Array.Copy(handshakeKeys, 0, sessionKey, 0, 8);
            Array.Copy(response, 4, sessionKey, 8, 8);
            lock_session.SetKey(sessionKey);

            // rekey the secure session as well
            secure_lock_session.SetKey(sessionKey);

            // send SEC_INITIALIZATION_COMMAND
            cmd = secure_lock_session.BuildCommand(0x03);
            Array.Copy(handshakeKeys, 8, cmd, 4, 8);
            response = await secure_lock_session.Execute(cmd);
            if (response[0] != 0x04)
            {
                throw new Exception("unexpected response to SEC_INITIALIZATION_COMMAND: " + BitConverter.ToString(response));
            }
            Debug("lock initialized");
        }

        public async Task ForceLock()
        {
            Debug("locking...");
            var cmd = lock_session.BuildCommand(0x0b);
            await this.lock_session.Execute(cmd);
        }

        public async Task ForceUnlock()
        {
            Debug("unlocking...");
            var cmd = lock_session.BuildCommand(0x0a);
            await lock_session.Execute(cmd);
        }

        public async Task Lock()
        {
            if (await this.Status() == "unlocked")
            {
                await this.ForceLock();
            }
        }

        public async Task Unlock()
        {
            if (await this.Status() == "locked")
            {
                await this.ForceUnlock();
            }
        }

        public async Task Toggle()
        {
            switch (await Status())
            {
                case "locked":
                    await ForceUnlock();
                    break;
                case "unlocked":
                    await ForceLock();
                    break;
            }
        }

        public async Task<string> Status()
        {
            Debug("status...");
            var cmd = new byte[18];
            cmd[0] = 0xee; // magic
            cmd[1] = 0x02;
            cmd[4] = 0x02;
            cmd[16] = 0x02;

            try
            {
                var response = await lock_session.Execute(cmd);
                var status = response[8];

                var strstatus = "unknown";
                if (status == 0x03)
                    strstatus = "unlocked";
                else if (status == 0x05)
                    strstatus = "locked";

                Debug(strstatus);
                return strstatus;
            }
            catch (Exception ex)
            {
                Debug(ex.Message + "\n" + ex.StackTrace);
                return "error";
            }
        }

        public async Task Disconnect()
        {
            Debug("disconnecting...");
            if (lock_session != null)
            {
                Func<Task> disconnect = async () =>
                {
                    var adapter = CrossBluetoothLE.Current.Adapter;
                    await adapter.DisconnectDeviceAsync(augustLock);
                };

                if (isSecure)
                {
                    isSecure = false;
                    var cmd = secure_lock_session.BuildCommand(0x05);
                    cmd[17] = 0x00;
                    var response = await secure_lock_session.Execute(cmd);
                    if (response[0] != 0x8b)
                    {
                        throw new Exception("unexpected response to DISCONNECT: " + BitConverter.ToString(response));
                    }
                }
                else
                {
                    await disconnect();
                }
            }
        }
    }
}
