namespace TeyPdfCad.AutoCAD;

internal static class TemplateEntityExpansion
{
    public static IReadOnlyList<T> Flatten<T>(
        T root,
        Func<T, bool> isSupported,
        Func<T, IReadOnlyList<T>> expand,
        int maximumDepth = 16)
    {
        if (isSupported is null) throw new ArgumentNullException(nameof(isSupported));
        if (expand is null) throw new ArgumentNullException(nameof(expand));
        if (maximumDepth < 0) throw new ArgumentOutOfRangeException(nameof(maximumDepth));

        var output = new List<T>();
        Visit(root, 0);
        return output;

        void Visit(T item, int depth)
        {
            if (isSupported(item))
            {
                output.Add(item);
                return;
            }

            var children = depth < maximumDepth ? expand(item) : [];
            if (children.Count == 0)
            {
                output.Add(item);
                return;
            }

            foreach (var child in children)
                Visit(child, depth + 1);
        }
    }
}
