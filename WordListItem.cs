using System.ComponentModel;

class WordListItem : INotifyPropertyChanged
{
    string draft;
    bool isEditing;

    public WordListItem(string word)
    {
        Word = word;
        draft = word;
    }

    public string Word { get; }

    public string Draft
    {
        get => draft;
        set
        {
            if (draft == value) return;
            draft = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Draft)));
        }
    }

    public bool IsEditing
    {
        get => isEditing;
        set
        {
            if (isEditing == value) return;
            isEditing = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
}
