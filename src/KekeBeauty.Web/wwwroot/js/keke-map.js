// Carte interactive OpenStreetMap (Leaflet) - coordonnees GPS reelles des etablissements.
// Interop minimal appele depuis les composants Blazor (Carte.razor, Home.razor,
// EtablissementDetailPage.razor) via IJSRuntime.
window.kekeMap = (function () {
    const instances = {};

    function init(elementId, centerLat, centerLng, zoom, markers) {
        destroy(elementId);

        const map = L.map(elementId).setView([centerLat, centerLng], zoom);

        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
            maxZoom: 19,
        }).addTo(map);

        (markers || []).forEach(function (m) {
            const marker = L.marker([m.lat, m.lng]).addTo(map);
            if (m.popupHtml) {
                marker.bindPopup(m.popupHtml);
            }
        });

        instances[elementId] = map;
        return true;
    }

    function destroy(elementId) {
        if (instances[elementId]) {
            instances[elementId].remove();
            delete instances[elementId];
        }
        delete instances[elementId + ':marker'];
    }

    // Carte avec marqueur deplacable (onboarding partenaire, feature enrichissement OSM) : clic ou
    // glisser-deposer pour affiner la position, avec rappel .NET (OnPinMoved) a chaque changement.
    function initPicker(elementId, lat, lng, zoom, dotnetRef) {
        destroy(elementId);

        const map = L.map(elementId).setView([lat, lng], zoom || 16);

        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
            maxZoom: 19,
        }).addTo(map);

        const marker = L.marker([lat, lng], { draggable: true }).addTo(map);
        const notify = function (position) {
            dotnetRef.invokeMethodAsync('OnPinMoved', position.lat, position.lng);
        };
        marker.on('dragend', function () { notify(marker.getLatLng()); });
        map.on('click', function (e) { marker.setLatLng(e.latlng); notify(e.latlng); });

        instances[elementId] = map;
        instances[elementId + ':marker'] = marker;
        return true;
    }

    function setPickerPosition(elementId, lat, lng) {
        const map = instances[elementId];
        const marker = instances[elementId + ':marker'];
        if (!map || !marker) return false;
        marker.setLatLng([lat, lng]);
        map.setView([lat, lng], map.getZoom());
        return true;
    }

    function getCurrentPosition() {
        if (!navigator.geolocation) return Promise.resolve({ status: 'unsupported' });
        return new Promise(resolve => navigator.geolocation.getCurrentPosition(
            position => resolve({ status: 'granted', latitude: position.coords.latitude, longitude: position.coords.longitude, accuracy: position.coords.accuracy }),
            error => resolve({ status: error.code === 1 ? 'denied' : 'unavailable' }),
            { enableHighAccuracy: true, timeout: 10000, maximumAge: 120000 }
        ));
    }

    function focus(elementId, latitude, longitude, zoom) {
        const map = instances[elementId];
        if (!map) return false;
        map.setView([latitude, longitude], zoom || 14);
        L.circleMarker([latitude, longitude], { radius: 8, color: '#410060', fillColor: '#e7b3ff', fillOpacity: 1, weight: 3 }).addTo(map).bindPopup('Votre position');
        return true;
    }

    return { init: init, destroy: destroy, getCurrentPosition: getCurrentPosition, focus: focus, initPicker: initPicker, setPickerPosition: setPickerPosition };
})();

