using Photobooth.Views;
using Xunit;

namespace Photobooth.Tests.Unit;

public class UndoRedoStackTests
{
    [Fact]
    public void NewStack_CanUndoAndCanRedoAreFalse()
    {
        var stack = new UndoRedoStack<string>();
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Push_MakesCanUndoTrue()
    {
        var stack = new UndoRedoStack<string>();
        stack.Push("state-a");
        Assert.True(stack.CanUndo);
    }

    [Fact]
    public void Undo_ReturnsPushedState_AndMakesCanRedoTrue()
    {
        var stack = new UndoRedoStack<string>();
        stack.Push("before");
        var result = stack.Undo("after");
        Assert.Equal("before", result);
        Assert.True(stack.CanRedo);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void Redo_ReturnsStateBackAfterUndo()
    {
        var stack = new UndoRedoStack<string>();
        stack.Push("before");
        var afterUndo = stack.Undo("after");
        var afterRedo = stack.Redo(afterUndo);
        Assert.Equal("after", afterRedo);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Push_ClearsRedoStack()
    {
        var stack = new UndoRedoStack<string>();
        stack.Push("state-1");
        stack.Undo("state-2");
        Assert.True(stack.CanRedo);

        stack.Push("state-3");
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Undo_WhenEmpty_ReturnsCurrentStateUnchanged()
    {
        var stack = new UndoRedoStack<string>();
        var result = stack.Undo("current");
        Assert.Equal("current", result);
    }

    [Fact]
    public void Redo_WhenEmpty_ReturnsCurrentStateUnchanged()
    {
        var stack = new UndoRedoStack<string>();
        var result = stack.Redo("current");
        Assert.Equal("current", result);
    }

    [Fact]
    public void Clear_ResetsBothStacks()
    {
        var stack = new UndoRedoStack<string>();
        stack.Push("state-1");
        stack.Undo("state-2");
        stack.Clear();
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }
}
