using Avalonia.Media.Imaging;
using Avalonia.Platform;

using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace RTSharp.Shared.Controls;

public enum BuiltInIcons
{
    RTSHARP,
    COMPUTER_KEY,
    DID_YOU_KNOW,
    DYNAMITE,
    FLAT_QUESTION_MARK,
    KEYS,
    SERVER,

    SERIOUS_EXCLAMATION,
    SERIOUS_INFO,
    SERIOUS_QUESTION,
    SERIOUS_X,
    SERIOUS2_INFO,
    SERIOUS2_X,

    VISTA_BLOCK,
    VISTA_EXCLAMATION,
    VISTA_INFO,
    VISTA_OK,
    VISTA_OK_EXCLAMATION,
    VISTA_OK_WAIT,
    VISTA_WAIT,
    VISTA_X,
    VISTA_X_EXCLAMATION,

    WIN10_EXCLAMATION,
    WIN10_EXCLAMATION_SHIELD,
    WIN10_INFO,
    WIN10_OK_SHIELD,
    WIN10_QUESTION_SHIELD,
    WIN10_X,
    WIN10_X_SHIELD
}

public static class BuiltInIcon
{
    private static readonly ConcurrentDictionary<BuiltInIcons, Bitmap> Cache = new();

    private static FrozenDictionary<BuiltInIcons, Uri> Icons = new Dictionary<BuiltInIcons, Uri>() {
        { BuiltInIcons.RTSHARP, new Uri("avares://RTSharp/Assets/rtsharp.ico") },
        { BuiltInIcons.COMPUTER_KEY, new Uri("avares://RTSharp/Assets/Icons/computer_key.ico") },
        { BuiltInIcons.DID_YOU_KNOW, new Uri("avares://RTSharp/Assets/Icons/did_you_know.ico") },
        { BuiltInIcons.DYNAMITE, new Uri("avares://RTSharp/Assets/Icons/dynamite.ico") },
        { BuiltInIcons.FLAT_QUESTION_MARK, new Uri("avares://RTSharp/Assets/Icons/flat_question_mark.ico") },
        { BuiltInIcons.KEYS, new Uri("avares://RTSharp/Assets/Icons/keys.ico") },
        { BuiltInIcons.SERVER, new Uri("avares://RTSharp/Assets/Icons/server.ico") },

        { BuiltInIcons.SERIOUS_EXCLAMATION, new Uri("avares://RTSharp/Assets/Icons/serious_exclamation.ico") },
        { BuiltInIcons.SERIOUS_INFO, new Uri("avares://RTSharp/Assets/Icons/serious_info.ico") },
        { BuiltInIcons.SERIOUS_QUESTION, new Uri("avares://RTSharp/Assets/Icons/serious_question.ico") },
        { BuiltInIcons.SERIOUS_X, new Uri("avares://RTSharp/Assets/Icons/serious_X.ico") },
        { BuiltInIcons.SERIOUS2_INFO, new Uri("avares://RTSharp/Assets/Icons/serious2_info.ico") },
        { BuiltInIcons.SERIOUS2_X, new Uri("avares://RTSharp/Assets/Icons/serious2_X.ico") },

        { BuiltInIcons.VISTA_BLOCK, new Uri("avares://RTSharp/Assets/Icons/vista_block.ico") },
        { BuiltInIcons.VISTA_EXCLAMATION, new Uri("avares://RTSharp/Assets/Icons/vista_exclamation.ico") },
        { BuiltInIcons.VISTA_INFO, new Uri("avares://RTSharp/Assets/Icons/vista_info.ico") },
        { BuiltInIcons.VISTA_OK, new Uri("avares://RTSharp/Assets/Icons/vista_ok.ico") },
        { BuiltInIcons.VISTA_OK_EXCLAMATION, new Uri("avares://RTSharp/Assets/Icons/vista_ok_exclamation.ico") },
        { BuiltInIcons.VISTA_OK_WAIT, new Uri("avares://RTSharp/Assets/Icons/vista_ok_wait.ico") },
        { BuiltInIcons.VISTA_WAIT, new Uri("avares://RTSharp/Assets/Icons/vista_wait.ico") },
        { BuiltInIcons.VISTA_X, new Uri("avares://RTSharp/Assets/Icons/vista_X.ico") },
        { BuiltInIcons.VISTA_X_EXCLAMATION, new Uri("avares://RTSharp/Assets/Icons/vista_X_exclamation.ico") },

        { BuiltInIcons.WIN10_EXCLAMATION, new Uri("avares://RTSharp/Assets/Icons/win10_exclamation.ico") },
        { BuiltInIcons.WIN10_EXCLAMATION_SHIELD, new Uri("avares://RTSharp/Assets/Icons/win10_exclamation_shield.ico") },
        { BuiltInIcons.WIN10_INFO, new Uri("avares://RTSharp/Assets/Icons/win10_info.ico") },
        { BuiltInIcons.WIN10_OK_SHIELD, new Uri("avares://RTSharp/Assets/Icons/win10_ok_shield.ico") },
        { BuiltInIcons.WIN10_QUESTION_SHIELD, new Uri("avares://RTSharp/Assets/Icons/win10_question_shield.ico") },
        { BuiltInIcons.WIN10_X, new Uri("avares://RTSharp/Assets/Icons/win10_X.ico") },
        { BuiltInIcons.WIN10_X_SHIELD, new Uri("avares://RTSharp/Assets/Icons/win10_X_shield.ico") },
    }.ToFrozenDictionary();

    public static Bitmap Get(BuiltInIcons Icon) => Cache.GetOrAdd(Icon, icon => new Bitmap(AssetLoader.Open(Icons[icon])));
}
