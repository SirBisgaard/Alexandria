# Connect a Kobo device to Alexandria

## Story

As a logged-in user of Alexandria, 
Wants to be able to add my Kobo device to the system.  
So that I am able to read or listen to my books in Alexandria.

## Technical 

In the frontend on the device page an option to pair a new device should be there.
When pressing pair a modal will show form where you can Enter a nickname of the device.
There will be a randomly generated Device Key (GUID) shown.
The ID is used for security as a key when the server is publicly available.
The modal will have some descriptive text on how the pairing process will work. 

When saved the new device with a nickname will show in the list of devices. 
With the Device Key used for connecting the device.
The row of the device will show unknown on Model name, Firmware and Last Sync.

(ON DEVICE)
Now the user will open the kobo configuration, and add the server url as shown below:
Example: https://alexandria.example.com/api/kobo/{devicekey}/

(ON DEVICE)
Now the user is able to press sync and the device will download the Alexandria Library. 

Authentication of a device is now handled by the Device Key in the route.

When the device connects we grab the latest device information from the headers in the sync endpoint.
We have the Device Key from the route it calls, and we will update the last sync on that endpoint as well.

The frontend will now display the device information.

The flow above is for one device. When another device is wish added, the simply doo it all again.
The device key will be different for each device, and will generate no conflicts. 

### Alternative Flow
When a device does not have a valid registered Device Key, then Alexandria should return 401. 

If a device is factory reset, the device key is always visible in the list of devices.
The key can be reused because of the pairing to the serial number. 

If another kobo device uses the same device key, we should block it and send 401.
We can only test that on the endpoints where the serial number is given to us in the headers.
There should be a locked property that can be unlocked in the frontend, with a description in a tooltip.
That another device is trying to use the device key. 

Unlocking a device does not remove the original serial number.

## Acceptance Criteria

- [ ] Device page has a "Pair new device" button that opens a modal
- [ ] Modal shows a nickname input, a generated Device Key, and pairing instructions
- [ ] Saving creates a device entry showing the nickname, Device Key, and "unknown" for Model, Firmware, and Last Sync
- [ ] A Kobo device configured with `https://{host}/api/kobo/{devicekey}/` can sync and receive the library
- [ ] On first sync, the device's model name, firmware, and last sync are populated from the request headers
- [ ] A request with an unregistered Device Key returns 401
- [ ] A request where the `serial number` header does not match the paired device returns 401 and the device is marked as locked
- [ ] A locked device can be unlocked from the frontend, with a tooltip explaining why it was locked
- [ ] The Device Key remains visible in the device list after the device is factory reset, allowing re-pairing

## Dependencies
The user is authenticated and logged into Alexandria.
Has a physical Kobo device. 