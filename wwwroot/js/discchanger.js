/*  Copyright 2020 Hugo Lyppens

    discchanger.js is part of DiscChanger.NET.

    DiscChanger.NET is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    DiscChanger.NET is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with DiscChanger.NET.  If not, see <https://www.gnu.org/licenses/>.
*/
"use strict";

var connection = new signalR.HubConnectionBuilder().withUrl("/discChangerHub").build();

const mainPlayerControls = ["play", "pause", "stop"];
const otherPlayerControls = ["previous", "next", "rev_scan", "fwd_scan", "time_text", "discs","scan","delete-discs",
    "disc_number", "title_album_number", "chapter_track_number", "disc_direct"];

const allPlayerControls = mainPlayerControls.concat(otherPlayerControls);

function updateControls(changer, slot, titleAlbumNumber, chapterTrackNumber, status, modeDisc) {
    var isOff = status == "off";
    var suffix = '_' + changer;
    allPlayerControls.forEach(function (item, i) { document.getElementById(item+suffix).parentElement.disabled = isOff; });
    mainPlayerControls.forEach(function (item, i) {
        if (item == status)
            document.getElementById(item + suffix).classList.add("btn-active");
        else
            document.getElementById(item + suffix).classList.remove("btn-active");
    });
    if (modeDisc == "all")
        document.getElementById("discs" + suffix).classList.add("btn-active");
    else
        document.getElementById("discs" + suffix).classList.remove("btn-active");
    var s = isOff ? ['btn-off', 'btn-on'] : ['btn-on', 'btn-off'];
    document.getElementById("power" + suffix).classList.add(s[0]);
    document.getElementById("power" + suffix).classList.remove(s[1]);
    document.getElementById("disc_number" + suffix).value = isOff ? null : slot;
    document.getElementById("title_album_number" + suffix).value = (isOff || titleAlbumNumber==0) ? null : titleAlbumNumber;
    document.getElementById("chapter_track_number" + suffix).value = (isOff || chapterTrackNumber==0) ? null : chapterTrackNumber;
    //switch (status) {
    //    case "off":
    //        break;
    //    case "stop":
    //    case "play":
    //    case "pause":
    //        document.getElementById(status + suffix).style.color = "Red";
    //        break;
    //    default:
    //}
}
function setup_popover(jq) {
    jq.popover({
        content: function () {
            return $(this).find(".data").html();
        },
        title: function () {
            return $(this).find(".artist").text() + '/' + $(this).find(".title").text() +
                '<button onclick="$(this).closest(\'div.popover\').popover(\'hide\');" class="close"><i class="far fa-window-close"></i></button>';
        },
        html: true,
        sanitize: false
    });
//    document.querySelectorAll('.config').forEach(function (e) { e.hidden = off; });
}

function updateDisc(newChanger, newSlot, discHtml) {
    var newSlotInt = parseInt(newSlot, 10);
    if (!isNaN(newSlotInt)) { newSlot = newSlotInt;}
    var changerElements = document.getElementsByClassName("disc-changer");
    var l = changerElements.length;
    var changerKey2Index = {}
    var i;
    for (i = 0; i < l; i++) {
        changerKey2Index[changerElements[i].dataset.key]=i;
    }
    var newChangerIndex = changerKey2Index[newChanger];
    var element = document.getElementById("discs-table")
    var position = 'beforeend';
    if (typeof newChangerIndex !== 'undefined') {
        var discElements = document.getElementsByClassName('disc');
        l = discElements.length;
        for (i = 0; i < l; i++) {
            var discElement = discElements[i];
            var ds = discElement.dataset;
            var changerIndex = changerKey2Index[ds.changer];
            if (changerIndex > newChangerIndex) { element = discElement; position = 'beforebegin'; break;}
            if (changerIndex == newChangerIndex) {
                var slot = ds.slot;
                if (slot > newSlot) { element = discElement; position = 'beforebegin'; break; }
                if (slot == newSlot) { document.removeChild(discElement); }
            }
        }
    }
    element.insertAdjacentHTML(position, discHtml);
    var insertedElement = (position === 'beforeend') ? element.lastElementChild : element.previousElementSibling;
    setup_popover($(insertedElement));
    element.scrollIntoView(false);
}

function scanStatus(changer, slot, index, count)
{
    document.getElementById("scan-status_" + changer).value = 'Scanning: ' + index + '/' + count + ', Slot: ' + slot;
}
                
function scanInProgress(changer, flag)
{
    document.getElementById("controls_" + changer).style.display = flag ? 'none' : 'block';
    document.getElementById("scan-controls_" + changer).style.display = flag ? 'block' : 'none';
    document.getElementById("scan-status_" + changer).value = flag ? 'Starting Disc Scan' : null;
    document.getElementById("disc_number_" + changer).style.readonly = flag;
    document.getElementById("title_album_number_" + changer).style.readonly = flag;
    document.getElementById("chapter_track_number_" + changer).style.readonly = flag;
    document.getElementById("disc_direct_" + changer).style.display = flag ? 'none' : 'inline-block';
}
function reload() {
    window.location.reload(true);
}

connection.on("StatusData", updateControls);
connection.on("DiscData", updateDisc);
connection.on("ScanStatus", scanStatus);
connection.on("ScanInProgress", scanInProgress);
connection.on("Reload", reload);


connection.start().then(function () {
//    document.getElementById("sendButton").disabled = false;
}).catch(function (err) {
    return console.error(err.toString());
});

function control(element) {
    var ds = element.dataset;
    connection.invoke("Control", ds.changerKey, ds.command).catch(function (err) {
        return console.error(err.toString());
    });
}

function scan(key, name) {
    $.ajax({
        url: '/?handler=DiscsToScan',
        data: {
            changerKey: key
        }
    }).done(function (result) {
        var discsToScan = prompt("which discs to scan on " + name, result);
        if (discsToScan) {
            connection.invoke("Scan", key, discsToScan).catch(function (err) {
                return console.error(err.toString());
            });
        }
    });
}
function confirmDeleteChanger(key, name) {
    var discsCount = document.getElementById('discs-table').querySelectorAll('[data-changer="' + key + '"]').length;
    return confirm("Do you want to delete changer " + name + " containing " + discsCount + " discs?");
}
function deleteChanger(key, name) {
    var discsCount = document.getElementById('discs-table').querySelectorAll('[data-changer="' + key + '"]').length;
    if (confirm("Do you want to delete changer " + name + " containing " + discsCount + " discs?")) {
        connection.invoke("DeleteChanger", key).catch(function (err) {
            return console.error(err.toString());
        });
    }
}
function moveChanger(key, offset) {
    connection.invoke("MoveChanger", key, offset).catch(function (err) {
        return console.error(err.toString());
    });
}
function deleteDiscs(key, name) {
    $.ajax({
        url: '/?handler=DiscsToDelete',
        data: {
            changerKey: key
        }
    }).done(function (result) {
        var discsToDelete = prompt("which discs to delete on " + name, result);
        if (discsToDelete) {
            connection.invoke("DeleteDiscs", key, discsToDelete).catch(function (err) {
                return console.error(err.toString());
            });
        }
    });
}

function cancelScan(element) {
    var key = element.dataset.changerKey;
    connection.invoke("CancelScan", key).catch(function (err) {
        return console.error(err.toString());
    });
}

function discDirect(key) {
    var slot = parseInt(document.getElementById("disc_number_" + key).value);
    if (isNaN(slot)) { slot = null; }
    var titleAlbumNumber = parseInt(document.getElementById("title_album_number_" + key).value);
    if (isNaN(titleAlbumNumber)) { titleAlbumNumber = null; }
    var chapterTrackNumber = parseInt(document.getElementById("chapter_track_number_" + key).value);
    if (isNaN(chapterTrackNumber)) { chapterTrackNumber = null; }
    connection.invoke("DiscDirect", key, slot, titleAlbumNumber, chapterTrackNumber).catch(function (err) {
        return console.error(err.toString());
    });
}
function dt(key, slot, chapterTrackNumber) {
    var suffix = '_' + key;
    var b = document.getElementById("power" + suffix).classList.contains('btn-on');
    document.getElementById("disc_number" + suffix).value = b ? slot : null;
    document.getElementById("title_album_number" + suffix).value = null;
    document.getElementById("chapter_track_number" + suffix).value = (b && chapterTrackNumber ) ? chapterTrackNumber:null;
}

function toggle_config(element) {
    //var off = element.classList.toggle('fa-toggle-off');
    //var on = element.classList.toggle('fa-toggle-on');
    var on = element.classList.toggle('btn-on');
    document.querySelectorAll('.config').forEach(function (e) { e.hidden = !on; });
    document.querySelectorAll('.hide-on-config').forEach(function (e) { e.hidden = on; });
}
function change_display_size(element) {
    var sz = element.value;
    var szrem = sz + 'rem';
    document.getElementById("discs-table").style.gridTemplateColumns = 'repeat(auto-fill, minmax(' + szrem + ',1fr))';
    element.title = "Disc Display Size (" + szrem + ")";
    document.cookie = 'DiscDisplaySize=' + sz + ';max-age=2000000000';
}
function clear_title_album_chapter_track(element) {
    var changerKey = element.dataset.changerKey;
    document.getElementById("title_album_number_" + changerKey).value = null;
    document.getElementById("chapter_track_number_" + changerKey).value = null;
}

