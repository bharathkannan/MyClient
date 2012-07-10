﻿using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SocketServer.TLS
{
    /// <summary>
    /// Computes a Hash-based Message Authentication Code (HMAC) using the <see cref="T:xBrainLab.Security.Cryptography.MD5" /> hash function
    /// </summary>
    public sealed class HMACMD5
    {
        private const int BLOCK_SIZE = 64;

        private byte[] m_Key = null;
        private byte[] m_inner = null;
        private byte[] m_outer = null;

      
        /// Initializes a new instance of the <see cref="HMACMD5"/> class the supplied key.
        /// </summary>
        /// <param name="key">The key.</param>
        public HMACMD5(byte[] key)
        {
            this.InitializeKey(key);
        }

        /// <summary>
        /// Gets or sets the key.
        /// </summary>
        /// <value>
        /// The key.
        /// </value>
        public byte[] Key
        {
            get
            {
                return this.m_Key;
            }
            set
            {
                this.InitializeKey(value);
            }
        }

        /// <summary>
        /// Computes the hash value for the specified byte array.
        /// </summary>
        /// <param name="buffer">The input to compute the hash code for.</param>
        /// <returns>
        /// The computed hash code
        /// </returns>
        public byte[] ComputeHash(byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer", "The input cannot be null.");
            }

            return MD5Core.GetHash(this.Combine(this.m_outer, MD5Core.GetHash(this.Combine(this.m_inner, buffer))));
        }

      

        /// <summary>
        /// Initializes the key.
        /// </summary>
        /// <param name="key">The key.</param>
        private void InitializeKey(byte[] key)
        {
            if (key == null)
            {
                throw new ArgumentNullException("key", "The Key cannot be null.");
            }

            if (key.Length > BLOCK_SIZE)
            {
                this.m_Key = MD5Core.GetHash(key);
            }
            else
            {
                this.m_Key = key;
            }

            this.UpdateIOPadBuffers();
        }

        /// <summary>
        /// Updates the IO pad buffers.
        /// </summary>
        private void UpdateIOPadBuffers()
        {
            if (this.m_inner == null)
            {
                this.m_inner = new byte[BLOCK_SIZE];
            }

            if (this.m_outer == null)
            {
                this.m_outer = new byte[BLOCK_SIZE];
            }

            for (int i = 0; i < BLOCK_SIZE; i++)
            {
                this.m_inner[i] = 54;
                this.m_outer[i] = 92;
            }

            for (int i = 0; i < this.Key.Length; i++)
            {
                byte[] s1 = this.m_inner;
                int s2 = i;
                s1[s2] ^= this.Key[i];
                byte[] s3 = this.m_outer;
                int s4 = i;
                s3[s4] ^= this.Key[i];
            }
        }

        /// <summary>
        /// Combines two array (a1 and a2).
        /// </summary>
        /// <param name="a1">The Array 1.</param>
        /// <param name="a2">The Array 2.</param>
        /// <returns>Combinaison of a1 and a2</returns>
        private byte[] Combine(byte[] a1, byte[] a2)
        {
            byte[] final = new byte[a1.Length + a2.Length];
            for (int i = 0; i < a1.Length; i++)
            {
                final[i] = a1[i];
            }

            for (int i = 0; i < a2.Length; i++)
            {
                final[i + a1.Length] = a2[i];
            }

            return final;
        }
    }

}
