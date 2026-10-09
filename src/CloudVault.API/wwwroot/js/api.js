const API_BASE = '/api';

const api = {
  getToken() {
    return localStorage.getItem('cv_access_token');
  },
  getRefreshToken() {
    return localStorage.getItem('cv_refresh_token');
  },
  getUser() {
    const userStr = localStorage.getItem('cv_user');
    return userStr ? JSON.parse(userStr) : null;
  },
  setSession(authData) {
    localStorage.setItem('cv_access_token', authData.accessToken);
    localStorage.setItem('cv_refresh_token', authData.refreshToken);
    localStorage.setItem('cv_user', JSON.stringify(authData.user));
  },
  clearSession() {
    localStorage.removeItem('cv_access_token');
    localStorage.removeItem('cv_refresh_token');
    localStorage.removeItem('cv_user');
  },
  isAuthenticated() {
    return !!this.getToken();
  },

  async request(endpoint, options = {}) {
    options.headers = options.headers || {};

    const token = this.getToken();
    if (token) {
      options.headers['Authorization'] = `Bearer ${token}`;
    }

    if (!(options.body instanceof FormData) && !options.headers['Content-Type'] && options.method && options.method !== 'GET') {
      options.headers['Content-Type'] = 'application/json';
    }

    // Force real-time updates without browser caching
    if (!options.method || options.method === 'GET') {
      options.cache = 'no-store';
      options.headers['Cache-Control'] = 'no-cache, no-store, must-revalidate';
      options.headers['Pragma'] = 'no-cache';
    }

    let response = await fetch(`${API_BASE}${endpoint}`, options);

    // Auto-refresh token on 401
    if (response.status === 401 && this.getRefreshToken()) {
      const refreshed = await this.refreshTokens();
      if (refreshed) {
        options.headers['Authorization'] = `Bearer ${this.getToken()}`;
        response = await fetch(`${API_BASE}${endpoint}`, options);
      } else {
        this.clearSession();
        window.location.href = '/login.html';
        throw new Error('Session expired. Please log in again.');
      }
    }

    if (response.status === 204) {
      return null;
    }

    const contentType = response.headers.get('content-type');
    if (contentType && contentType.includes('application/json')) {
      const result = await response.json();
      if (!response.ok) {
        let msg = result.message || 'Request failed';
        if (result.errors) {
          const detailed = Object.values(result.errors).flat().join(' ');
          if (detailed) msg = `${msg} ${detailed}`;
        }
        throw new Error(msg);
      }
      return result.data !== undefined ? result.data : result;
    }

    if (!response.ok) {
      throw new Error(`HTTP error ${response.status}`);
    }

    return response;
  },

  async refreshTokens() {
    try {
      const rf = this.getRefreshToken();
      if (!rf) return false;

      const res = await fetch(`${API_BASE}/auth/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: rf })
      });

      if (!res.ok) return false;
      const data = await res.json();
      if (data.success && data.data) {
        this.setSession(data.data);
        return true;
      }
      return false;
    } catch {
      return false;
    }
  },

  // Auth Methods
  async login(email, password) {
    const data = await this.request('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password })
    });
    this.setSession(data);
    return data;
  },

  async register(fullName, email, password, confirmPassword) {
    const data = await this.request('/auth/register', {
      method: 'POST',
      body: JSON.stringify({ fullName, email, password, confirmPassword })
    });
    this.setSession(data);
    return data;
  },

  async logout() {
    try {
      const rf = this.getRefreshToken();
      if (rf) {
        await this.request('/auth/logout', {
          method: 'POST',
          body: JSON.stringify({ refreshToken: rf })
        });
      }
    } catch (e) {
      console.warn('Logout request warning:', e);
    } finally {
      this.clearSession();
      window.location.href = '/login.html';
    }
  },

  async getMe() {
    return await this.request('/auth/me', { method: 'GET' });
  },

  // Profile Methods
  async getProfile() {
    return await this.request('/profile', { method: 'GET' });
  },

  async updateProfile(fullName) {
    return await this.request('/profile', {
      method: 'PUT',
      body: JSON.stringify({ fullName })
    });
  },

  async uploadProfilePhoto(file) {
    const formData = new FormData();
    formData.append('photo', file);
    return await this.request('/profile/photo', {
      method: 'POST',
      body: formData
    });
  },

  async changePassword(currentPassword, newPassword, confirmNewPassword) {
    return await this.request('/profile/password', {
      method: 'PUT',
      body: JSON.stringify({ currentPassword, newPassword, confirmNewPassword })
    });
  },

  // Storage Methods
  async getStorageUsage() {
    return await this.request('/storage/usage', { method: 'GET' });
  },

  // Folder Methods
  async getFolders(parentId = null) {
    const query = parentId ? `?parentId=${parentId}` : '';
    return await this.request(`/folders${query}`, { method: 'GET' });
  },

  async getFolderById(id) {
    return await this.request(`/folders/${id}`, { method: 'GET' });
  },

  async createFolder(name, parentFolderId = null) {
    return await this.request('/folders', {
      method: 'POST',
      body: JSON.stringify({ name, parentFolderId })
    });
  },

  async renameFolder(id, name) {
    return await this.request(`/folders/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ name })
    });
  },

  async deleteFolder(id) {
    return await this.request(`/folders/${id}`, { method: 'DELETE' });
  },

  // File Methods
  async getFiles(params = {}) {
    const qs = new URLSearchParams();
    if (params.search) qs.append('search', params.search);
    if (params.folderId) qs.append('folderId', params.folderId);
    if (params.fileType) qs.append('fileType', params.fileType);
    if (params.page) qs.append('page', params.page);
    if (params.pageSize) qs.append('pageSize', params.pageSize);
    if (params.sortBy) qs.append('sortBy', params.sortBy);
    if (params.sortDescending !== undefined) qs.append('sortDescending', params.sortDescending);
    if (params.includeSubfolders !== undefined) qs.append('includeSubfolders', params.includeSubfolders);

    return await this.request(`/files?${qs.toString()}`, { method: 'GET' });
  },

  async getFileById(id) {
    return await this.request(`/files/${id}`, { method: 'GET' });
  },

  async uploadFile(file, folderId = null, onProgress = null) {
    const formData = new FormData();
    formData.append('file', file);
    if (folderId) formData.append('folderId', folderId);

    // Using XMLHttpRequest for accurate upload progress if provided
    if (onProgress) {
      return new Promise((resolve, reject) => {
        const xhr = new XMLHttpRequest();
        xhr.open('POST', `${API_BASE}/files/upload`);
        const token = api.getToken();
        if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`);

        xhr.upload.onprogress = (e) => {
          if (e.lengthComputable) {
            const percent = Math.round((e.loaded / e.total) * 100);
            onProgress(percent);
          }
        };

        xhr.onload = () => {
          try {
            const json = JSON.parse(xhr.responseText);
            if (xhr.status >= 200 && xhr.status < 300) {
              resolve(json.data);
            } else {
              reject(new Error(json.message || 'Upload failed'));
            }
          } catch (err) {
            reject(new Error('Failed to parse server response'));
          }
        };

        xhr.onerror = () => reject(new Error('Network error during upload'));
        xhr.send(formData);
      });
    }

    return await this.request('/files/upload', {
      method: 'POST',
      body: formData
    });
  },

  async uploadMultipleFiles(files, folderId = null) {
    const formData = new FormData();
    for (const file of files) {
      formData.append('files', file);
    }
    if (folderId) formData.append('folderId', folderId);

    return await this.request('/files/upload-multiple', {
      method: 'POST',
      body: formData
    });
  },

  async deleteFile(id) {
    return await this.request(`/files/${id}`, { method: 'DELETE' });
  },

  async renameFile(id, newFileName) {
    return await this.request(`/files/${id}/rename`, {
      method: 'PUT',
      body: JSON.stringify({ newFileName })
    });
  },

  async moveFile(id, targetFolderId) {
    return await this.request(`/files/${id}/move`, {
      method: 'PUT',
      body: JSON.stringify({ targetFolderId })
    });
  },

  async getDownloadInfo(id) {
    return await this.request(`/files/${id}/download`, { method: 'GET' });
  },

  getProfilePhotoUrl() {
    const token = this.getToken();
    return `${API_BASE}/profile/photo?token=${encodeURIComponent(token || '')}`;
  },

  getFileViewUrl(id) {
    const token = this.getToken();
    return `${API_BASE}/files/${id}/view?token=${encodeURIComponent(token || '')}`;
  },

  getFileDownloadUrl(id) {
    const token = this.getToken();
    return `${API_BASE}/files/${id}/download?direct=true&token=${encodeURIComponent(token || '')}`;
  },

  async downloadFile(id, fileName) {
    let token = this.getToken();
    let res = await fetch(`${API_BASE}/files/${id}/download?direct=true`, {
      method: 'GET',
      headers: token ? { 'Authorization': `Bearer ${token}` } : {}
    });

    if (res.status === 401 && this.getRefreshToken()) {
      const refreshed = await this.refreshTokens();
      if (refreshed) {
        token = this.getToken();
        res = await fetch(`${API_BASE}/files/${id}/download?direct=true`, {
          method: 'GET',
          headers: token ? { 'Authorization': `Bearer ${token}` } : {}
        });
      }
    }

    if (!res.ok) {
      let errorMsg = `Download failed with HTTP status ${res.status}`;
      try {
        const errorJson = await res.json();
        if (errorJson && errorJson.message) errorMsg = errorJson.message;
      } catch (_) {}
      throw new Error(errorMsg);
    }

    const blob = await res.blob();
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName || 'download';
    document.body.appendChild(a);
    a.click();
    setTimeout(() => {
      window.URL.revokeObjectURL(url);
      a.remove();
    }, 1000);
  },

  openFile(id) {
    const url = this.getFileViewUrl(id);
    const win = window.open(url, '_blank');
    if (!win) {
      window.location.href = url;
    }
  }
};

// UI Utilities
function showToast(message, type = 'info') {
  let container = document.getElementById('toast-container');
  if (!container) {
    container = document.createElement('div');
    container.id = 'toast-container';
    document.body.appendChild(container);
  }

  const toast = document.createElement('div');
  toast.className = `cv-toast ${type}`;
  toast.innerHTML = `
    <span>${message}</span>
    <button type="button" style="background:none;border:none;color:white;cursor:pointer;font-size:1.1rem;margin-left:0.75rem;" onclick="this.parentElement.remove()">&times;</button>
  `;

  container.appendChild(toast);
  setTimeout(() => toast.remove(), 4000);
}

function formatBytes(bytes) {
  if (!bytes || bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return `${(bytes / Math.pow(1024, i)).toFixed(2)} ${units[i]}`;
}

function formatDate(dateString) {
  if (!dateString) return '';
  const d = new Date(dateString);
  return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}

function getFileIcon(fileName) {
  const ext = (fileName || '').split('.').pop().toLowerCase();
  switch (ext) {
    case 'pdf': return '📄';
    case 'doc':
    case 'docx': return '📝';
    case 'xls':
    case 'xlsx': return '📊';
    case 'ppt':
    case 'pptx': return '📑';
    case 'jpg':
    case 'jpeg':
    case 'png':
    case 'gif':
    case 'webp': return '🖼️';
    case 'zip': return '📦';
    case 'txt': return '📃';
    default: return '📁';
  }
}
