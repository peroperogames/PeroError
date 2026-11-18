using PeroTools2.Error;
using PeroTools2.Error.Harness;

// IoErr_NotFound and IoErr_Timeout are automatically generated
// by PeroTools2.PeroError.Generator based on [ErrMsg] and [ErrArg].
var notFound = IoErr_NotFound.New("/tmp/a.txt");
Console.WriteLine($"kind    = {notFound.kind}");
Console.WriteLine($"path    = {notFound.path}");
Console.WriteLine($"message = {notFound.message}");
Console.WriteLine();

var timeout = IoErr_Timeout.New("/tmp/b.txt", 1.5);
Console.WriteLine($"kind    = {timeout.kind}");
Console.WriteLine($"message = {timeout.message}");
Console.WriteLine();

// // The companion class remains Err<TKind>, and it can participate normally in Result / Wrap / AddNote.
Result<string, IoErr> result = notFound;
Console.WriteLine($"result  = {result}");
Console.WriteLine($"isOK    = {result.TryGetValue(out string? _)}");
Console.WriteLine();

var wrapped = IoErr_Timeout.New("/tmp/b.txt", 3.0, causedBy: notFound);
wrapped.AddNote("retry later");
Console.WriteLine(wrapped.ToString());
// Even when no additional data is needed, you can still directly create an error using `Err.New
var simple = Err.New(IoErr.NotFound, "raw message");
Console.WriteLine($"simple  = {simple.kind}: {simple.message}");