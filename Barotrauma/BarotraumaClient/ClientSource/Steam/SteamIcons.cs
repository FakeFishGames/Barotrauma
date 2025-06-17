#nullable enable
using System;

namespace Barotrauma
{
    /// <summary>
    /// Contains all available Steam timeline event icons as constants and helper methods for number icons.
    /// </summary>
    public static class SteamIcons
    {
        // Standard icons
        public const string Marker = "steam_marker";
        public const string Achievement = "steam_achievement";
        public const string Attack = "steam_attack";
        public const string Bolt = "steam_bolt";
        public const string Bookmark = "steam_bookmark";
        public const string Bug = "steam_bug";
        public const string Cart = "steam_cart";
        public const string Caution = "steam_caution";
        public const string Chat = "steam_chat";
        public const string Checkmark = "steam_checkmark";
        public const string Chest = "steam_chest";
        public const string Circle = "steam_circle";
        public const string Combat = "steam_combat";
        public const string Completed = "steam_completed";
        public const string Crown = "steam_crown";
        public const string Death = "steam_death";
        public const string Defend = "steam_defend";
        public const string Diamond = "steam_diamond";
        public const string Edit = "steam_edit";
        public const string Effect = "steam_effect";
        public const string Explosion = "steam_explosion";
        public const string Fix = "steam_fix";
        public const string Flag = "steam_flag";
        public const string Gem = "steam_gem";
        public const string Group = "steam_group";
        public const string Heart = "steam_heart";
        public const string Info = "steam_info";
        public const string Invalid = "steam_invalid";
        public const string Minus = "steam_minus";
        public const string Pair = "steam_pair";
        public const string Person = "steam_person";
        public const string Plus = "steam_plus";
        public const string Purchase = "steam_purchase";
        public const string Question = "steam_question";
        public const string Ribbon = "steam_ribbon";
        public const string Screenshot = "steam_screenshot";
        public const string Scroll = "steam_scroll";
        public const string Square = "steam_square";
        public const string Star = "steam_star";
        public const string Starburst = "steam_starburst";
        public const string Timer = "steam_timer";
        public const string Transfer = "steam_transfer";
        public const string Triangle = "steam_triangle";
        public const string Trophy = "steam_trophy";
        public const string View = "steam_view";
        public const string X = "steam_x";

        // Common number icons
        public const string Zero = "steam_0";
        public const string One = "steam_1";
        public const string Two = "steam_2";
        public const string Three = "steam_3";
        public const string Four = "steam_4";
        public const string Five = "steam_5";
        public const string Six = "steam_6";
        public const string Seven = "steam_7";
        public const string Eight = "steam_8";
        public const string Nine = "steam_9";
        public const string Ten = "steam_10";

        /// <summary>
        /// Gets the Steam icon name for a number between 0 and 99.
        /// </summary>
        /// <param name="number">The number to get the icon for (0-99)</param>
        /// <returns>The Steam icon name in the format "steam_X"</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the number is less than 0 or greater than 99</exception>
        public static string GetNumberIcon(int number)
        {
            if (number is < 0 or > 99)
            {
                throw new ArgumentOutOfRangeException(nameof(number), "Number must be between 0 and 99");
            }
            return $"steam_{number}";
        }
    }
} 