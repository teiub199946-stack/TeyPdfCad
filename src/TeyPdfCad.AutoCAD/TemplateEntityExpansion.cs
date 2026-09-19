namespace TeyPdfCad.AutoCAD;

internal static class TemplateEntityExpansion
{
    public static void VisitLeaves<T>(
        T root,
        Func<T, bool> isSupported,
        Func<T, IReadOnlyList<T>> expand,
        Action<T> visit,
        Action<T> releaseGenerated,
        int maximumDepth = 16)
    {
        if (isSupported is null) throw new ArgumentNullException(nameof(isSupported));
        if (expand is null) throw new ArgumentNullException(nameof(expand));
        if (visit is null) throw new ArgumentNullException(nameof(visit));
        if (releaseGenerated is null) throw new ArgumentNullException(nameof(releaseGenerated));
        if (maximumDepth < 0) throw new ArgumentOutOfRangeException(nameof(maximumDepth));

        Visit(root, 0, generated: false);
        return;

        void Visit(T item, int depth, bool generated)
        {
            try
            {
                if (isSupported(item))
                {
                    visit(item);
                    return;
                }

                var children = depth < maximumDepth ? expand(item) : [];
                if (children.Count == 0)
                {
                    visit(item);
                    return;
                }

                foreach (var child in children)
                    Visit(child, depth + 1, generated: true);
            }
            finally
            {
                if (generated)
                    releaseGenerated(item);
            }
        }
    }

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
