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

var connection = new signalR.HubConnectionBuilder().withUrl("/discChangerHub").withAutomaticReconnect().build();
//configureLogging(signalR.LogLevel.Debug).

const mainPlayerControls = ["open", "play", "pause", "stop"];
const otherPlayerControls = ["previous", "next", "rev_scan", "fwd_scan", "time_text", "discs","scan","delete-discs",
    "disc_number", "title_album_number", "chapter_track_number", "disc_direct"];
const allPlayerControls = mainPlayerControls.concat(otherPlayerControls);

function updateControls(changer, slot, titleAlbumNumber, chapterTrackNumber, status, modeDisc) {
    var isOff = (status == "off");
    var suffix = '_' + changer;
	var item;
    for(item of allPlayerControls) {
        var buttonElement = document.getElementById(item + suffix);
        var b = isOff || buttonElement.dataset.disabled;
        buttonElement.disabled = b;
    }
    for(item of mainPlayerControls) {
        document.getElementById(item + suffix).classList.toggle("btn-active", item == status && !isOff);
    }
    document.getElementById("discs" + suffix).classList.toggle("btn-active", modeDisc == "all" && !isOff);
    document.getElementById("power" + suffix).classList.toggle('btn-power-on', !isOff);
    document.getElementById("disc_number" + suffix).value = isOff ? null : slot;
    document.getElementById("title_album_number" + suffix).value = (isOff || titleAlbumNumber==0) ? null : titleAlbumNumber;
    document.getElementById("chapter_track_number" + suffix).value = (isOff || chapterTrackNumber==0) ? null : chapterTrackNumber;
}

function setup_metadata_dialog(jq) {
    jq.on('show.bs.modal', function (event) {
        var button = $(event.relatedTarget); // Button that triggered the modal
        var changerName = button.data('changer-name'); // Extract info from data-* attributes
        var changerKey = button.data('changer-key'); // Extract info from data-* attributes
        // If necessary, you could initiate an AJAX request here (and then do the updating in a callback).
        // Update the modal's content. We'll use jQuery here, but you could use a data binding library or other methods instead.
        var modal = this;
        modal.querySelector('#metaDataModalLabel').textContent = 'Retrieve metadata for ' + changerName;
        var sel = modal.querySelector('#metadata-selection');
        var btn = modal.querySelector('#metadata-start-retrieval');
        var slotsSet = modal.querySelector('#slots-set');
        sel.selectedIndex = -1;
        slotsSet.value = null;
        slotsSet.disabled = true;
        btn.disabled = true;
        sel.onchange = function () {
            slotsSet.disabled = false;
            $.ajax({
                url: '/?handler=MetaDataNeeded',
                data: {
                    changerKey: changerKey,
                    metadataType: sel.value
                }
            }).done(function (result) {
                slotsSet.value = result;
                btn.disabled = !result;
            }).fail(function (xhr, textStatus, errorThrown) {
                console.error(xhr.responseText);
                alert(xhr.responseText);
            });
        };
        slotsSet.oninput = function () { btn.disabled = !slotsSet.value;}
        btn.onclick = function ()
        {
            var s = slotsSet.value;
            var m = sel.value;
            if (s && m) {
                connection.invoke("RetrieveMetaData", changerKey, m, s).catch(function (err) {
                    return console.error(err.toString());
                });
            }
        }
    });
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
        i=0;
        while( i < discElements.length ) {
            var discElement = discElements[i];
            var ds = discElement.dataset;
            var changerIndex = changerKey2Index[ds.changer];
            if (changerIndex > newChangerIndex) { element = discElement; position = 'beforebegin'; break;}
            if (changerIndex == newChangerIndex) {
                var slot = ds.slot;
                if (slot > newSlot) { element = discElement; position = 'beforebegin'; break; }
                if (slot == newSlot) { $(discElement).popover('dispose');discElement.remove(); continue; }
            }
            i++;
        }
    }
    element.insertAdjacentHTML(position, discHtml);
    var insertedElement = (position === 'beforeend') ? element.lastElementChild : element.previousElementSibling;
    new bootstrap.Popover(insertedElement, { sanitize: false });
    insertedElement.scrollIntoView(false);
}

function opStatus(op, changer, slot, index, count)
{
    document.getElementById("scan-status_" + changer).value = op+': ' + index + '/' + count + ', Slot: ' + slot;
}
                
function opInProgress(changer, op)
{
    var b = Boolean(op);
    document.getElementById("controls_" + changer).style.display = b ? 'none' : 'block';
    document.getElementById("scan-controls_" + changer).style.display = b ? 'block' : 'none';
    document.getElementById("scan-status_" + changer).value = b ? 'Starting '+op : null;
    document.getElementById("disc_number_" + changer).style.readonly = b;
    document.getElementById("title_album_number_" + changer).style.readonly = b;
    document.getElementById("chapter_track_number_" + changer).style.readonly = b;
    document.getElementById("disc_direct_" + changer).style.display = b ? 'none' : 'inline-block';
}

function reload() {
    window.location.reload(true);
}

connection.on("StatusData", updateControls);
connection.on("DiscData", updateDisc);
connection.on("OpStatus", opStatus);
connection.on("OpInProgress", opInProgress);
connection.on("Reload", reload);
connection.start().then(function () {
}).catch(function (err) {
    return console.error(err.toString());
});

function scroll_into_view(changerKey) {
    var query = '[data-changer="' + changerKey + '"]';
    var slot = document.getElementById("disc_number_" + changerKey).value;
    if (slot) {
        query += '[data-slot="' + slot + '"]';
    }
    var discElement = document.querySelector(query);
    if (discElement) {
        discElement.scrollIntoView(false);
    }
}

function control(changerKey,command) {
    if (command) {
        connection.invoke("Control", changerKey, command).catch(function (err) {
            return console.error(err.toString());
        });
    }
}
function getFirstLine(str) {
    var breakIndexR = str.indexOf("\r");
    var breakIndexN = str.indexOf("\n");
    var breakIndexMax = Math.max(breakIndexN, breakIndexR);
    if (breakIndexMax===-1) 
        return str;
    var breakIndexMin = Math.min(breakIndexN, breakIndexR);
    var breakIndex = breakIndexMin === -1 ? breakIndexMax : breakIndexMin;
    return str.substr(0, breakIndex);
}

function scan(key, name) {
    $.ajax({
        url: '/?handler=DiscsToScan',
        data: {
            changerKey: key
        }
    }).done(function (result) {
        var discsToScan = prompt("Which discs to scan on " + name + "?\n(Empty slots: " + result.emptySlots + ")", result.discsToScan);
        if (discsToScan) {
            connection.invoke("Scan", key, discsToScan).catch(function (err) {
                return console.error(err.toString());
            });
        }
    }).fail(function (xhr, textStatus, errorThrown) {
        console.error(xhr.responseText);
        alert(xhr.responseText);
    });
}


function deleteChanger(key, name) {
    var discsCount = document.getElementById('discs-table').querySelectorAll('[data-changer="' + key + '"]').length;
    if (confirm("Do you want to delete changer " + name + " containing " + discsCount + " discs?")) {
        connection.invoke("DeleteChanger", key).catch(function (err) {
            return console.error(err.toString());
        });
    }
}

function shiftDiscs(key, name) {
    $.ajax({
        url: '/?handler=AvailableSlots',
        data: {
            changerKey: key
        }
    }).done(function (availableSlotsSet) {
        var slotsSet = prompt("Which set of discs to shift on " + name + "?\n(Available slots: " + availableSlotsSet + ")");
        if (slotsSet) {
            $.ajax({
                url: '/?handler=PopulatedSlots',
                data: {
                    changerKey: key,
                    slotsSet: slotsSet
                }
            }).done(function (populatedSlotsSet) {
                if (!populatedSlotsSet) {
                    alert("All slots empty"); return;
                }
                var offset = parseInt(prompt("Integer offset to shift discs by in: " + name + ", slots: " + populatedSlotsSet + "?\n(Available slots: " + availableSlotsSet + ")"), 10);
                if (!offset) {
                    alert("Cancelled"); return;
                }
                $.ajax({
                    url: '/?handler=ValidateDiscShift',
                    data: {
                        changerKey: key,
                        slotsSet: populatedSlotsSet,
                        offset: offset
                    }
                }).done(function (shiftedSlotsSet) {
                    if (!shiftedSlotsSet) {
                        alert("Invalid disc shift request"); return;
                    }
                    if (confirm('Please confirm shifting discs on ' + name + ' from ' + populatedSlotsSet + ' to ' + shiftedSlotsSet)) {
                        connection.invoke("ShiftDiscs", key, populatedSlotsSet, shiftedSlotsSet).catch(function (err) {
                            return console.error(err.toString());
                        });
                    }
                }).fail(function (xhr, textStatus, errorThrown) {
                    console.error(xhr.responseText);
                    alert(getFirstLine(xhr.responseText));
                });
            }).fail(function (xhr, textStatus, errorThrown) {
                console.error(xhr.responseText);
                alert(getFirstLine(xhr.responseText));
            });
        }
    }).fail(function (xhr, textStatus, errorThrown) {
        console.error(xhr.responseText);
        alert(getFirstLine(xhr.responseText));
    });
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
        var discsToDelete = prompt("Which discs to delete on " + name + "?\n(Empty slots: " + result.emptySlots + ")", result.discsToDelete);
        if (discsToDelete) {
            connection.invoke("DeleteDiscs", key, discsToDelete).catch(function (err) {
                return console.error(err.toString());
            });
        }
    }).fail(function (xhr, textStatus, errorThrown) {
        console.error(xhr.responseText);
        alert(getFirstLine(xhr.responseText));
    });
}

function cancelOp(changerKey) {
    connection.invoke("CancelOp", changerKey).catch(function (err) {
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
    connection.invoke("DiscDirectAsync", key, slot, titleAlbumNumber, chapterTrackNumber).catch(function (err) {
        return console.error(err.toString());
    });
}

function dt(key, slot, chapterTrackNumber) {
    var suffix = '_' + key;
    var b = document.getElementById("power" + suffix).classList.contains('btn-power-on');
    document.getElementById("disc_number" + suffix).value = b ? slot : null;
    document.getElementById("title_album_number" + suffix).value = null;
    document.getElementById("chapter_track_number" + suffix).value = (b && chapterTrackNumber ) ? chapterTrackNumber:null;
}

function toggle_config() {
    var on = document.getElementById("edit").classList.toggle('btn-active');
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

function clear_title_album_chapter_track(changerKey) {
    document.getElementById("title_album_number_" + changerKey).value = null;
    document.getElementById("chapter_track_number_" + changerKey).value = null;
}
