var atelierProjectLibrary =
{
    $AtelierDownloadBlob: function (name, blob)
    {
        var url;
        var link;

        try
        {
            url = URL.createObjectURL(blob);
            link = document.createElement('a');
            link.href = url;
            link.download = name;
            link.hidden = true;

            document.body.appendChild(link);
            link.click();
            return 1;
        }
        catch (error)
        {
            return 0;
        }
        finally
        {
            if (link)
            {
                link.remove();
            }

            if (url)
            {
                setTimeout(revokeUrl, 1000);
            }
        }

        function revokeUrl()
        {
            URL.revokeObjectURL(url);
        }
    },

    AtelierDownloadProject__deps: ['$AtelierDownloadBlob'],
    AtelierDownloadProject: function (name, json)
    {
        var fileName = UTF8ToString(name);
        var options =
        {
            type: 'application/json'
        };
        var blob = new Blob([UTF8ToString(json)], options);

        return AtelierDownloadBlob(fileName, blob);
    },

    AtelierDownloadPng__deps: ['$AtelierDownloadBlob'],
    AtelierDownloadPng: function (name, png, size)
    {
        if (size <= 0)
        {
            return 0;
        }

        var bytes = HEAPU8.slice(png, png + size);
        var fileName = UTF8ToString(name);
        var options =
        {
            type: 'image/png'
        };
        var blob = new Blob([bytes], options);

        return AtelierDownloadBlob(fileName, blob);
    },

    AtelierPickProject: function (maxBytes, callback)
    {
        var input = document.createElement('input');
        input.type = 'file';
        input.accept = '.json,application/json';
        input.hidden = true;

        document.body.appendChild(input);

        var listenerOptions =
        {
            once: true
        };
        input.addEventListener('cancel', cancel, listenerOptions);
        input.addEventListener('change', readFile, listenerOptions);

        try
        {
            // showPicker reports blocked browser activation so the import button can be used again.
            if (input.showPicker && window.top === window.self)
            {
                input.showPicker();
            }
            else
            {
                input.click();
            }

            return 1;
        }
        catch (error)
        {
            input.remove();
            return 0;
        }

        function finish(result, pointer, length)
        {
            input.remove();
            {{{ makeDynCall('viii', 'callback') }}}(result, pointer, length);
        }

        function cancel()
        {
            finish(0, 0, 0);
        }

        function readFile()
        {
            var file = input.files[0];
            if (!file)
            {
                finish(0, 0, 0);
                return;
            }

            if (file.size > maxBytes)
            {
                finish(2, 0, 0);
                return;
            }

            file.arrayBuffer().then(copyFile, reportReadError);
        }

        function copyFile(buffer)
        {
            var length = buffer.byteLength;
            var pointer = length > 0 ? _malloc(length) : 0;
            if (length > 0 && !pointer)
            {
                reportReadError();
                return;
            }

            try
            {
                if (pointer)
                {
                    HEAPU8.set(new Uint8Array(buffer), pointer);
                }

                finish(1, pointer, length);
            }
            finally
            {
                if (pointer)
                {
                    _free(pointer);
                }
            }
        }

        function reportReadError()
        {
            finish(3, 0, 0);
        }
    }
};

mergeInto(LibraryManager.library, atelierProjectLibrary);
