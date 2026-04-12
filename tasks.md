_# Tasks for Alexandria


**FAQ and how Alexandria works**
- A new page / modal that show how the epub and m4b files are mapped.
  - A modal with tabs for each file.
  - Use pre tags to show the tags in file and point to what it wil be in the book DTO.

**Make Alexandria ready for public use**

- Default user called: Alexander / TheGreat
- User system for authentication Table
  - UserUuid
  - Username
  - Password - Hashed with seed GUID as salt.
  - DisplayName
  - CreatedAt
  - ModifiedAt
  - LastLoginAt
- Pages for system
  - Login page
  - Users list page
  - Devices list page. (Button to enable device pair for 5min)
  - Edit device modal. (Devices cannot be created)
  - Edit/Create user modal
- Authentication
  - Use Framework as much as possible. Fx JWT tokens. Save in cookie in frontend.
  - All Alexandria controllers (Not Kobo device controllers) should use Authentication.
  - Devices that is added should get a signed device cookie — HMAC(deviceId + issuedAt, serverSecret), verified on every request_
  - Our Cookie should not be forwarded to kobo at all. 
  - Method to allow unknown device to connect.
  - Block all unknown device ids

- Device Table
  - DeviceUuid 
  - DeviceId => x-kobo-deviceid => 884b742f345078bdb96054a0455aeb58abfda14b1414cf9dfd2c79429ab6b1c4
  - ModelName => UserAgent => Lookup table of existing devices
  - ModelCode => UserAgent => Trim code to 4 didgets => 0390
  - Firmware => UserAgent
  - CreatedAt
  - ModifiedAt
  - LastRequestAt



### Context

x-kobo-deviceid
- SHA-256 hash: 884b742f345078bdb96054a0455aeb58abfda14b1414cf9dfd2c79429ab6b1c4
- Present on 59/204 reqs — all API calls except cover image downloads
- Single value across all reqs → unique per physical device
- Likely derived from device serial number