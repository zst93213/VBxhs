using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security;

namespace Vista.Accounts.Vault
{
    /// <summary>
    /// 凭证保险箱。设计计划 §九"账号凭证经 DPAPI（CurrentUser）加密落盘"的落地。
    /// DPAPI CurrentUser scope：只有当前 Windows 用户能解密，进程内存外不可读。
    /// 可选二次密码加密（MasterPasswordVault）留作后续扩展。
    /// </summary>
    public sealed class SecureCredentialVault
    {
        /// <summary>
        /// 用 DPAPI（CurrentUser）加密凭证字节。
        /// </summary>
        public byte[] Protect(byte[] plaintext)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            return ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
        }

        /// <summary>用 DPAPI（CurrentUser）解密。</summary>
        public byte[] Unprotect(byte[] ciphertext)
        {
            if (ciphertext == null) throw new ArgumentNullException(nameof(ciphertext));
            return ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);
        }

        /// <summary>
        /// 把 SecureString 在内存中转为 byte[] 并立即清零中间 buffer。
        /// 用于"登录后从内存中的密码到加密存储"的过渡。
        /// </summary>
        public static byte[] SecureStringToBytes(SecureString s)
        {
            if (s == null) return new byte[0];
            var bytes = new byte[s.Length * 2];
            var handle = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(s);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(handle, bytes, 0, bytes.Length);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(handle);
                Array.Clear(bytes, 0, bytes.Length); // 调用方应在使用后再次清零
            }
            return bytes;
        }
    }
}
