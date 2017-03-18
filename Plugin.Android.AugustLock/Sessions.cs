using Plugin.BLE.Abstractions.Contracts;
using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.Android.AugustLock
{
    // ported from https://github.com/ethitter/augustctl/blob/master/lib/lock_session.js
    class LockSession
    {
        private AugustLockDevice device;
        private ICryptoTransform encryptCipher;
        private ICryptoTransform decryptCipher;
        protected CipherMode cipherMode = CipherMode.CBC;
        protected byte[] iv = new byte[16];
        private byte[] readData;
        private ManualResetEvent readEvent = new ManualResetEvent(true);

        public ICharacteristic WriteCharacteristic { get; set; }
        public ICharacteristic ReadCharacteristic { get; set; }
        public int CommandTimeout { get; set; }

        public LockSession(AugustLockDevice parent)
        {
            CommandTimeout = 3000;
            device = parent;
        }

        public void SetKey(byte[] key)
        {
            var aes = new AesManaged();
            aes.Mode = cipherMode;
            aes.Padding = PaddingMode.None;
            aes.Key = key;
            aes.IV = this.iv;
            encryptCipher = aes.CreateEncryptor();
            decryptCipher = aes.CreateDecryptor();
        }

        public async Task Start()
        {
            ReadCharacteristic.ValueUpdated += (obj, args) =>
            {
                var buf = args.Characteristic.Value;
                readData = new byte[buf.Length];
                buf.CopyTo(readData, 0);

                if (decryptCipher != null)
                {
                    device.Debug("encrypted data: " + BitConverter.ToString(readData));
                    decryptCipher.TransformBlock(readData, 0, 16, readData, 0);
                }
                device.Debug("received data: " + BitConverter.ToString(readData));
                readEvent.Set();
            };

            var sTask = ReadCharacteristic.StartUpdatesAsync();
            if (await Task.WhenAny(sTask, Task.Delay(5000)) == sTask)
            {
                await sTask;
            }
            else
            {
                device.Debug("read listener startup timeout");
                throw new Exception("Read listener startup timeout");
            }
        }

        public virtual byte[] BuildCommand(byte opcode)
        {
            var cmd = new byte[18];
            cmd[0] = 0xee; // magic
            cmd[1] = opcode;
            cmd[16] = 0x02; // unknown
            return cmd;
        }

        public int SimpleChecksum(byte[] buf)
        {
            int cs = 0;
            for (int i = 0; i < 18; i++)
            {
                cs = (cs + buf[i]) & 0xff;
            }
            return (-cs) & 0xff;
        }

        protected virtual void WriteChecksum(byte[] command)
        {
            command[3] = (byte)SimpleChecksum(command);
        }

        protected virtual void ValidateResponse(byte[] response)
        {
            if (SimpleChecksum(response) != 0)
            {
                throw new Exception("simple checksum mismatch");
            }
            if (response[0] != 0xbb && response[0] != 0xaa)
            {
                throw new Exception("unexpected magic in response");
            }
        }

        private async Task<bool> Write(byte[] command)
        {
            // NOTE: the last two bytes are not encrypted
            // general idea seems to be that if the last byte of the command indicates an offline key offset (is non-zero), the command is "secure" and encrypted with the offline key
            if (encryptCipher != null)
            {
                encryptCipher.TransformBlock(command, 0, 16, command, 0);
                device.Debug("write (encrypted): " + BitConverter.ToString(command));
            }
            // write the command to the write characteristic
            return await WriteCharacteristic.WriteAsync(command);
        }

        public async Task<byte[]> Execute(byte[] command)
        {
            // reset the wait event
            readEvent.Reset();
            WriteChecksum(command);
            device.Debug("execute command: " + BitConverter.ToString(command));
            var wTask = Write(command);
            if (await Task.WhenAny(wTask, Task.Delay(CommandTimeout)) == wTask)
            {
                await wTask;
            }
            else
            {
                throw new Exception("command execution timeout");
            }
            device.Debug("write successful");

            return await Task.Run<byte[]>(() =>
            {
                // process response
                if (!readEvent.WaitOne(CommandTimeout))
                {
                    throw new Exception("command execute response timeout");
                }
                byte[] data = new byte[readData.Length];
                readData.CopyTo(data, 0);
                ValidateResponse(data);
                return data;
            });

        }
    }

    // ported from https://github.com/ethitter/augustctl/blob/master/lib/secure_lock_session.js
    class SecureLockSession : LockSession
    {
        public byte OfflineKeyOffset { get; set; }

        public SecureLockSession(AugustLockDevice parent) : base(parent)
        {
            cipherMode = CipherMode.ECB;
        }

        public override byte[] BuildCommand(byte opcode)
        {
            var cmd = new byte[18];
            cmd[0] = opcode;
            cmd[16] = 0x0f; // unknown
            cmd[17] = OfflineKeyOffset;
            return cmd;
        }

        private UInt32 ReadUInt32LE(byte[] buffer, int startIndex)
        {
            var buf = new byte[4];
            Array.Copy(buffer, startIndex, buf, 0, 4);
            return BitConverter.ToUInt32(buf, 0);
        }

        private void WriteUInt32LE(byte[] buffer, uint value, int startIndex)
        {
            var buf = BitConverter.GetBytes(value);
            Array.Copy(buf, 0, buffer, startIndex, 4);
        }

        public uint SecurityChecksum(byte[] buffer)
        {
            return (0 - (ReadUInt32LE(buffer, 0x00) + ReadUInt32LE(buffer, 0x04) + ReadUInt32LE(buffer, 0x08))) >> 0;
        }

        protected override void WriteChecksum(byte[] command)
        {
            var cs = SecurityChecksum(command);
            WriteUInt32LE(command, cs, 0x0c);
        }

        protected override void ValidateResponse(byte[] data)
        {
            if (SecurityChecksum(data) != ReadUInt32LE(data, 0x0c))
            {
                throw new Exception("security checksum mismatch");
            }
        }
    }
}