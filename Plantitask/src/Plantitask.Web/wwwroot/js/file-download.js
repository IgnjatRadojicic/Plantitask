// Attachment downloads go through an authorized API call, so the browser cannot simply
// navigate to a URL. C# fetches the bytes with the bearer token attached and hands them
// here; this turns them into a blob and clicks a throwaway link.
window.fileDownload = {

    save: function (fileName, contentType, bytes) {
        const blob = new Blob([bytes], { type: contentType || 'application/octet-stream' });
        const url = URL.createObjectURL(blob);

        const link = document.createElement('a');
        link.href = url;
        link.download = fileName || 'download';
        document.body.appendChild(link);
        link.click();

        document.body.removeChild(link);
        URL.revokeObjectURL(url);
    }
}
