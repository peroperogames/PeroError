namespace PeroTools2.Error.Harness;

/// <summary>Example: errors declared with attributes; companion classes are generated automatically</summary>
internal enum IOErr
{
    /// <summary>The file does not exist; generates <c>IOErr_NotFound</c> automatically</summary>
    [ErrMsg("File {path} Do Not Exist")]
    [ErrArg("path", typeof(string), "Path that was not found")]
    NotFound,

    /// <summary>The read timed out; generates <c>IOErr_Timeout</c> automatically</summary>
    [ErrMsg("Read {path} Timeout After {timeout:F1}s")]
    [ErrArg("path", typeof(string), "Path that timed out")]
    [ErrArg("timeout", typeof(double), "Timeout in seconds")]
    Timeout
}