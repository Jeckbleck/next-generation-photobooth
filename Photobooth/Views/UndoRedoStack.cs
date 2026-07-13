using System.Collections.Generic;

namespace Photobooth.Views;

public sealed class UndoRedoStack<T>
{
    private readonly Stack<T> _undo = new();
    private readonly Stack<T> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(T stateBeforeChange)
    {
        _undo.Push(stateBeforeChange);
        _redo.Clear();
    }

    public T Undo(T currentState)
    {
        if (!CanUndo) return currentState;
        _redo.Push(currentState);
        return _undo.Pop();
    }

    public T Redo(T currentState)
    {
        if (!CanRedo) return currentState;
        _undo.Push(currentState);
        return _redo.Pop();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
