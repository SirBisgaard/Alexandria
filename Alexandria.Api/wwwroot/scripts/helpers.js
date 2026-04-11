// ===== Constants =====
var COVER_LARGE = { w: 400, h: 600 };
var COVER_SMALL = { w: 120, h: 180 };

var coverCacheBusters = {};

function bustCoverCache(uuid) {
    coverCacheBusters[uuid] = Date.now();
}

function coverUrl(uuid, width, height) {
    var url = "/api/alexandria/books/" + uuid + "/cover";
    if (width && height) { url += "?width=" + width + "&height=" + height; }
    var t = coverCacheBusters[uuid];
    if (t) { url += (url.indexOf("?") >= 0 ? "&" : "?") + "t=" + t; }
    return url;
}

var LANGUAGES = {
    "af": "Afrikaans", "ar": "Arabic", "bg": "Bulgarian", "bn": "Bengali", "ca": "Catalan",
    "cs": "Czech", "cy": "Welsh", "da": "Danish", "de": "German", "el": "Greek",
    "en": "English", "es": "Spanish", "et": "Estonian", "fa": "Persian", "fi": "Finnish",
    "fr": "French", "ga": "Irish", "gl": "Galician", "he": "Hebrew", "hi": "Hindi",
    "hr": "Croatian", "hu": "Hungarian", "id": "Indonesian", "is": "Icelandic", "it": "Italian",
    "ja": "Japanese", "ka": "Georgian", "ko": "Korean", "lt": "Lithuanian", "lv": "Latvian",
    "mk": "Macedonian", "ms": "Malay", "nl": "Dutch", "no": "Norwegian", "pl": "Polish",
    "pt": "Portuguese", "ro": "Romanian", "ru": "Russian", "sk": "Slovak", "sl": "Slovenian",
    "sq": "Albanian", "sr": "Serbian", "sv": "Swedish", "th": "Thai", "tr": "Turkish",
    "uk": "Ukrainian", "vi": "Vietnamese", "zh": "Chinese"
};

function languageName(code) {
    if (!code) { return ""; }
    return LANGUAGES[code.toLowerCase()] || code;
}

function languageOptions(selected) {
    var html = '<option value=""' + (!selected ? ' selected' : '') + '></option>';
    for (var code in LANGUAGES) {
        html += '<option value="' + code + '"' + (selected && selected.toLowerCase() === code ? ' selected' : '') + '>' + LANGUAGES[code] + '</option>';
    }
    return html;
}

function formatFileSize(bytes) {
    if (!bytes) { return ""; }
    return (bytes / (1024 * 1024)).toFixed(1) + " MB";
}

function bookCountText(books) {
    var ebooks = 0, audiobooks = 0;
    for (var i = 0; i < books.length; i++) {
        if (books[i].mediaType === "Audiobook")
            audiobooks++;
        else
            ebooks++;
    }
    var parts = [];
    if (ebooks > 0)
        parts.push(ebooks + (ebooks === 1 ? " book" : " books"));
    if (audiobooks > 0)
        parts.push(audiobooks + (audiobooks === 1 ? " audiobook" : " audiobooks"));
    return parts.join(", ") || "0 books";
}

function readingStatusText(progress, mediaType) {
    var isAudiobook = mediaType === "Audiobook";
    if (!progress || progress.status === "ReadyToRead") { return isAudiobook ? "Unplayed" : "Unread"; }
    if (progress.status === "Finished" || (progress.progressPercent != null && Math.round(progress.progressPercent) >= 100)) { return "Finished"; }
    if (progress.progressPercent != null) { return Math.max(1, Math.min(100, Math.round(progress.progressPercent))) + (isAudiobook ? "% Played" : "% Read"); }
    return progress.status;
}

function readingStatusRaw(progress) {
    if (!progress || progress.status === "ReadyToRead") { return "ReadyToRead"; }
    return progress.status;
}

function statusMenuItems(uuid, rawStatus, isArchived) {
    var items = "";
    if (rawStatus !== "Finished") {
        items += '<li><a class="dropdown-item" href="#" data-mark-finished="' + uuid + '">Mark as finished</a></li>';
    }
    if (rawStatus === "Reading" || rawStatus === "Finished") {
        items += '<li><a class="dropdown-item" href="#" data-mark-unread="' + uuid + '">Mark as unread</a></li>';
    }
    items += '<li><a class="dropdown-item" href="#" data-edit-book="' + uuid + '">Edit Book</a></li>';
    if (isArchived) {
        items += '<li><a class="dropdown-item" href="#" data-unarchive="' + uuid + '">Unarchive</a></li>';
    } else {
        items += '<li><a class="dropdown-item" href="#" data-archive="' + uuid + '">Archive</a></li>';
    }
    return items;
}

// ===== Cover & HTML Helpers =====
function fallbackColor(uuid) {
    var hash = 0;
    for (var i = 0; i < uuid.length; i++) {
        hash = uuid.charCodeAt(i) + ((hash << 5) - hash);
    }
    var h = Math.abs(hash) % 360;
    return "hsl(" + h + ", 30%, 35%)";
}

var AUDIOBOOK_BADGE = '<svg class="audiobook-badge" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg"><circle cx="12" cy="12" r="11" fill="#000" stroke="#fff" stroke-width="1"/><path d="M12 5.5a5.8 5.8 0 0 0-5.8 5.8v3.2a1.6 1.6 0 0 0 1.6 1.6h1.2a.8.8 0 0 0 .8-.8v-2.8a.8.8 0 0 0-.8-.8H7.8v-1.2a4.2 4.2 0 0 1 8.4 0V12.5h-1.2a.8.8 0 0 0-.8.8v2.8a.8.8 0 0 0 .8.8h1.2a1.6 1.6 0 0 0 1.6-1.6v-3.2A5.8 5.8 0 0 0 12 5.5z" fill="#fff"/></svg>';

function bookCoverHtml(book, cssClass) {
    var cls = cssClass || "book-cover-img";
    var size = cls === "book-list-cover-img" ? COVER_SMALL : COVER_LARGE;
    var hasCover = !!book.hasCover;
    var badge = book.mediaType === "Audiobook" ? AUDIOBOOK_BADGE : "";
    return '<div class="cover-frame ' + cls + '">' +
           (hasCover ? '<img loading="lazy" src="' + coverUrl(book.bookUuid, size.w, size.h) + '" alt="" onerror="this.style.display=\'none\';this.nextElementSibling.style.display=\'flex\';">' : '') +
           '<div class="book-cover" style="' + (hasCover ? 'display:none;' : '') + 'background:' + fallbackColor(book.bookUuid) + ';">' + escapeHtml(book.title) + '<small>' + escapeHtml((book.authors || []).map(function(a){return a.name}).join(", ")) + '</small></div>' +
           badge + '</div>';
}

function homeStackCoverHtml(book) {
    return '<div class="home-cover-slot">' + bookCoverHtml(book) + '</div>';
}

function homeSingleCoverHtml(book) {
    return '<div class="home-cover-single-slot">' + bookCoverHtml(book) + '</div>';
}

function escapeHtml(text) {
    if (!text) { return ""; }
    return $("<span>").text(text).html();
}

function highlightJson(obj) {
    var raw = JSON.stringify(obj, null, 2);
    var escaped = raw.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    return escaped.replace(/("(?:\\.|[^"\\])*")\s*:|"(?:\\.|[^"\\])*"|\b(-?\d+\.?\d*(?:[eE][+-]?\d+)?)\b|\b(true|false)\b|\bnull\b/g, function (match, key, num, bool) { if (key) { return "<span class='json-key'>" + key + "</span>:"; } if (match[0] === '"') { return "<span class='json-string'>" + match + "</span>"; } if (num) { return "<span class='json-number'>" + match + "</span>"; } if (bool) { return "<span class='json-bool'>" + match + "</span>"; } return "<span class='json-null'>" + match + "</span>"; });
}
