using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using CommunityToolkit.Mvvm.ComponentModel;

namespace RTSharp.Shared.Controls.ViewModels
{
    public enum WAITING_BOX_ICON
    {
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

    public partial class WaitingBoxViewModel : ObservableObject
    {
        private static Dictionary<WAITING_BOX_ICON, Uri> Icons = new Dictionary<WAITING_BOX_ICON, Uri>() {
            { WAITING_BOX_ICON.SERIOUS_EXCLAMATION, new Uri("avares://RTSharp/Assets/Icons/serious_exclamation.ico") },
            { WAITING_BOX_ICON.SERIOUS_INFO, new Uri("avares://RTSharp/Assets/Icons/serious_info.ico") },
            { WAITING_BOX_ICON.SERIOUS_QUESTION, new Uri("avares://RTSharp/Assets/Icons/serious_question.ico") },
            { WAITING_BOX_ICON.SERIOUS_X, new Uri("avares://RTSharp/Assets/Icons/serious_X.ico") },
            { WAITING_BOX_ICON.SERIOUS2_INFO, new Uri("avares://RTSharp/Assets/Icons/serious2_info.ico") },
            { WAITING_BOX_ICON.SERIOUS2_X, new Uri("avares://RTSharp/Assets/Icons/serious2_X.ico") },
            { WAITING_BOX_ICON.VISTA_BLOCK, new Uri("avares://RTSharp/Assets/Icons/vista_block.ico") },
            { WAITING_BOX_ICON.VISTA_EXCLAMATION, new Uri("avares://RTSharp/Assets/Icons/vista_exclamation.ico") },
            { WAITING_BOX_ICON.VISTA_INFO, new Uri("avares://RTSharp/Assets/Icons/vista_info.ico") },
            { WAITING_BOX_ICON.VISTA_OK, new Uri("avares://RTSharp/Assets/Icons/vista_ok.ico") },
            { WAITING_BOX_ICON.VISTA_OK_EXCLAMATION, new Uri("avares://RTSharp/Assets/Icons/vista_ok_exclamation.ico") },
            { WAITING_BOX_ICON.VISTA_OK_WAIT, new Uri("avares://RTSharp/Assets/Icons/vista_ok_wait.ico") },
            { WAITING_BOX_ICON.VISTA_WAIT, new Uri("avares://RTSharp/Assets/Icons/vista_wait.ico") },
            { WAITING_BOX_ICON.VISTA_X, new Uri("avares://RTSharp/Assets/Icons/vista_X.ico") },
            { WAITING_BOX_ICON.VISTA_X_EXCLAMATION, new Uri("avares://RTSharp/Assets/Icons/vista_X_exclamation.ico") },
            { WAITING_BOX_ICON.WIN10_EXCLAMATION, new Uri("avares://RTSharp/Assets/Icons/win10_exclamation.ico") },
            { WAITING_BOX_ICON.WIN10_EXCLAMATION_SHIELD, new Uri("avares://RTSharp/Assets/Icons/win10_exclamation_shield.ico") },
            { WAITING_BOX_ICON.WIN10_INFO, new Uri("avares://RTSharp/Assets/Icons/win10_info.ico") },
            { WAITING_BOX_ICON.WIN10_OK_SHIELD, new Uri("avares://RTSharp/Assets/Icons/win10_ok_shield.ico") },
            { WAITING_BOX_ICON.WIN10_QUESTION_SHIELD, new Uri("avares://RTSharp/Assets/Icons/win10_question_shield.ico") },
            { WAITING_BOX_ICON.WIN10_X, new Uri("avares://RTSharp/Assets/Icons/win10_X.ico") },
            { WAITING_BOX_ICON.WIN10_X_SHIELD, new Uri("avares://RTSharp/Assets/Icons/win10_X_shield.ico") },
        };


        public string Text { get; }
        public Bitmap Image { get; }

        public WaitingBoxViewModel(string Text, string Description, WAITING_BOX_ICON Icon)
        {
            this.Text = Text;
            this.Description = Description;
            Image = new Bitmap(AssetLoader.Open(Icons[Icon]));
        }

        [ObservableProperty]
        public int progress;

        [ObservableProperty]
        public string description;
    }
}
