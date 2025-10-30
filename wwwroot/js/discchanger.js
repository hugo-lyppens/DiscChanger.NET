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

// Event delegation for popover Go button
document.addEventListener('click', function(e) {
    // Handle Go button in popover
    if (e.target.classList.contains('disc-go-btn')) {
        var key = e.target.getAttribute('data-changer');
        var slot = e.target.getAttribute('data-slot'); // Keep as string for input field
        if (key && slot) {
            // Populate the slot number in the main control panel
            var slotInput = document.getElementById('disc_number_' + key);
            if (slotInput) {
                slotInput.value = slot;
            }
            // Clear title/album and chapter/track fields
            var titleAlbumInput = document.getElementById('title_album_number_' + key);
            if (titleAlbumInput) {
                titleAlbumInput.value = '';
            }
            var chapterTrackInput = document.getElementById('chapter_track_number_' + key);
            if (chapterTrackInput) {
                chapterTrackInput.value = '';
            }

            // Trigger the main discDirect function
            discDirect(key);

            // Close any open popovers
            var popovers = document.querySelectorAll('[data-bs-toggle="popover"]');
            popovers.forEach(function(popover) {
                var popoverInstance = bootstrap.Popover.getInstance(popover);
                if (popoverInstance) {
                    popoverInstance.hide();
                }
            });
        }
    }
});

// Library name editing
function editLibraryName() {
    var title = document.getElementById('library-title');
    var input = document.getElementById('library-title-input');
    input.value = title.innerText;
    title.style.display = 'none';
    input.style.display = 'block';
    input.focus();
    input.select();
}

function saveLibraryName() {
    var title = document.getElementById('library-title');
    var input = document.getElementById('library-title-input');
    var newName = input.value.trim() || 'Your Library';
    title.innerText = newName;
    title.style.display = 'block';
    input.style.display = 'none';
    // Save to cookie
    document.cookie = 'LibraryName=' + encodeURIComponent(newName) + ';max-age=2000000000';
}

// Load library name from cookie on page load
document.addEventListener('DOMContentLoaded', function() {
    var cookies = document.cookie.split(';');
    for (var i = 0; i < cookies.length; i++) {
        var cookie = cookies[i].trim();
        if (cookie.startsWith('LibraryName=')) {
            var name = decodeURIComponent(cookie.substring(12));
            document.getElementById('library-title').innerText = name;
            break;
        }
    }
});

// Toggle CD/Movie sections with cookie persistence
var hideCDs = false;
var hideMovies = false;

// Load toggle states from cookies on page load
function loadToggleStates() {
    var cookies = document.cookie.split(';');
    for (var i = 0; i < cookies.length; i++) {
        var cookie = cookies[i].trim();
        if (cookie.startsWith('HideCDs=')) {
            hideCDs = cookie.substring(8) === 'true';
        }
        if (cookie.startsWith('HideMovies=')) {
            hideMovies = cookie.substring(11) === 'true';
        }
    }
    updateToggleButtons();
    applyFilters();
}

function toggleCDSection() {
    hideCDs = !hideCDs;
    document.cookie = 'HideCDs=' + hideCDs + ';max-age=2000000000';
    updateToggleButtons();
    applyFilters();
}

function toggleMovieSection() {
    hideMovies = !hideMovies;
    document.cookie = 'HideMovies=' + hideMovies + ';max-age=2000000000';
    updateToggleButtons();
    applyFilters();
}

function updateToggleButtons() {
    var cdBtn = document.getElementById('toggle-cds');
    var movieBtn = document.getElementById('toggle-movies');
    
    if (cdBtn) {
        if (hideCDs) {
            cdBtn.classList.remove('active');
            cdBtn.classList.add('inactive');
        } else {
            cdBtn.classList.add('active');
            cdBtn.classList.remove('inactive');
        }
    }
    
    if (movieBtn) {
        if (hideMovies) {
            movieBtn.classList.remove('active');
            movieBtn.classList.add('inactive');
        } else {
            movieBtn.classList.add('active');
            movieBtn.classList.remove('inactive');
        }
    }
}

function applyFilters() {
    var discs = document.querySelectorAll('.disc');
    discs.forEach(function(disc) {
        var mediaType = disc.getAttribute('data-media-type');
        var shouldHide = (hideCDs && mediaType === 'CD') || (hideMovies && mediaType === 'MOVIE');
        disc.style.display = shouldHide ? 'none' : 'flex';
    });
}

// Call loadToggleStates when page loads
document.addEventListener('DOMContentLoaded', function() {
    loadToggleStates();
});

// Random disc selection - just highlight, don't load
function selectRandomCD() {
    var cdDiscs = document.querySelectorAll('.disc[data-media-type="CD"]');
    if (cdDiscs.length === 0) {
        alert('No CDs found!');
        return;
    }
    
    // Remove previous highlight
    document.querySelectorAll('.disc').forEach(function(d) {
        d.classList.remove('disc-selected');
    });
    
    var randomDisc = cdDiscs[Math.floor(Math.random() * cdDiscs.length)];
    randomDisc.classList.add('disc-selected');
    
    // Scroll to the disc
    randomDisc.scrollIntoView({ behavior: 'smooth', block: 'center' });
}

function selectRandomMovie() {
    var movieDiscs = document.querySelectorAll('.disc[data-media-type="MOVIE"]');
    if (movieDiscs.length === 0) {
        alert('No movies found!');
        return;
    }
    
    // Remove previous highlight
    document.querySelectorAll('.disc').forEach(function(d) {
        d.classList.remove('disc-selected');
    });
    
    var randomDisc = movieDiscs[Math.floor(Math.random() * movieDiscs.length)];
    randomDisc.classList.add('disc-selected');
    
    // Scroll to the disc
    randomDisc.scrollIntoView({ behavior: 'smooth', block: 'center' });
}

// Smart random - selects based on what's currently visible
function selectSmartRandom() {
    // Check which sections are visible
    var cdVisible = !hideCDs;
    var movieVisible = !hideMovies;
    
    // If both are visible or hidden, pick from all
    if ((cdVisible && movieVisible) || (!cdVisible && !movieVisible)) {
        // Pick randomly between CD and Movie
        if (Math.random() < 0.5) {
            selectRandomCD();
        } else {
            selectRandomMovie();
        }
    } else if (cdVisible) {
        selectRandomCD();
    } else {
        selectRandomMovie();
    }
}

// Search functionality - move matching results to top
function searchLibrary() {
    var searchText = document.getElementById('library-search').value.toLowerCase();
    var discsContainer = document.getElementById('discs-table');
    var discs = Array.from(document.querySelectorAll('.disc'));
    
    if (searchText === '') {
        // Reset all discs to visible and restore original order
        discs.forEach(function(disc) {
            disc.style.order = '';
            disc.style.opacity = '1';
            disc.style.pointerEvents = 'auto';
        });
        return;
    }
    
    // Score each disc based on match
    discs.forEach(function(disc) {
        var artist = disc.querySelector('.artist')?.innerText.toLowerCase() || '';
        var title = disc.querySelector('.title')?.innerText.toLowerCase() || '';
        var slot = disc.getAttribute('data-slot') || '';
        var changer = disc.querySelector('.disc-header .slot')?.innerText.toLowerCase() || '';
        
        var matches = artist.includes(searchText) || 
                     title.includes(searchText) || 
                     slot.includes(searchText) ||
                     changer.includes(searchText);
        
        if (matches) {
            disc.style.order = '-1';
            disc.style.opacity = '1';
            disc.style.pointerEvents = 'auto';
        } else {
            disc.style.order = '999';
            disc.style.opacity = '0.3';
            disc.style.pointerEvents = 'auto';
        }
    });
}

// Toggle navbar between full and compact mode
function toggleNavbar() {
    var mainNavbar = document.getElementById('main-navbar');
    var compactBar = document.getElementById('compact-bar');
    var isHidden = mainNavbar.style.display === 'none';
    
    if (isHidden) {
        mainNavbar.style.display = 'block';
        compactBar.style.display = 'none';
        document.cookie = 'navbarHidden=false; path=/; max-age=31536000';
    } else {
        mainNavbar.style.display = 'none';
        compactBar.style.display = 'block';
        document.cookie = 'navbarHidden=true; path=/; max-age=31536000';
    }
}

// Load navbar state from cookie on page load
function loadNavbarState() {
    var cookies = document.cookie.split(';');
    for (var i = 0; i < cookies.length; i++) {
        var cookie = cookies[i].trim();
        if (cookie.startsWith('navbarHidden=')) {
            var isHidden = cookie.substring('navbarHidden='.length) === 'true';
            if (isHidden) {
                document.getElementById('main-navbar').style.display = 'none';
                document.getElementById('compact-bar').style.display = 'block';
            }
            return;
        }
    }
}

// Close other popovers when opening a new one and add glowing effect
function setupPopoverBehavior() {
    // Get all elements with popovers
    var popoverElements = document.querySelectorAll('[data-bs-toggle="popover"]');
    
    popoverElements.forEach(function(element) {
        element.addEventListener('show.bs.popover', function() {
            // Close all other popovers and remove their glow
            popoverElements.forEach(function(otherElement) {
                if (otherElement !== element) {
                    var popoverInstance = bootstrap.Popover.getInstance(otherElement);
                    if (popoverInstance) {
                        popoverInstance.hide();
                    }
                    otherElement.classList.remove('disc-selected');
                }
            });
            
            // Add glowing effect to the opened disc
            element.classList.add('disc-selected');
        });
        
        element.addEventListener('hide.bs.popover', function() {
            // Remove glowing effect when popover closes
            element.classList.remove('disc-selected');
        });
    });
}

// Initialize on page load
document.addEventListener('DOMContentLoaded', function() {
    loadNavbarState();
    setupPopoverBehavior();
});
