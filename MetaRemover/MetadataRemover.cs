// File: MetadataRemover.cs
using System;
using System.Text;

namespace MetaRemover
{
    /// <summary>
    /// Contains the core logic for finding and removing Comfizen-specific metadata from files.
    /// </summary>
    public static class MetadataRemover
    {
        // This marker must be identical to the one used in the main Comfizen application.
        private const string MagicMarker = "COMFIZEN_WORKFLOW_EMBED_V1";
        private static readonly byte[] MagicMarkerBytes = Encoding.UTF8.GetBytes(MagicMarker);

        /// <summary>
        /// Removes the embedded Comfizen workflow from a byte array if it exists.
        /// </summary>
        /// <param name="fileBytes">The byte array of the file (image or video).</param>
        /// <returns>
        /// A new byte array with the metadata removed, or null if no metadata was found.
        /// </returns>
        public static byte[]? RemoveComfizenMetadata(byte[] fileBytes)
        {
            // Find the last occurrence of our magic marker. We search from the end for robustness.
            int markerIndex = FindLast(fileBytes, MagicMarkerBytes);

            // If the marker is found, it means our metadata is appended.
            if (markerIndex != -1)
            {
                // Create a new array containing only the original file data (up to the marker).
                byte[] cleanedBytes = new byte[markerIndex];
                Array.Copy(fileBytes, 0, cleanedBytes, 0, markerIndex);
                return cleanedBytes;
            }

            // If the marker is not found, return null to indicate that no changes were made.
            return null;
        }

        /// <summary>
        /// Finds the last index of a byte sequence (needle) within a larger byte array (haystack).
        /// </summary>
        /// <returns>The starting index of the needle, or -1 if not found.</returns>
        private static int FindLast(byte[] haystack, byte[] needle)
        {
            if (needle.Length > haystack.Length) return -1;
            for (int i = haystack.Length - needle.Length; i >= 0; i--)
            {
                bool found = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }
    }
}