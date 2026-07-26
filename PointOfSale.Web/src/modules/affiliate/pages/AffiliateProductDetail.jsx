import React, { useEffect, useMemo, useState } from 'react'
import { useParams, Link, useNavigate } from 'react-router-dom'
import axios from 'axios'
import { sanitizeRichTextHtml } from '@/utils/htmlSanitizer'
import { affiliateAuthApi } from '../../../services/affiliateAuthApi'

const AffiliateProductDetail = () => {
    const { slug } = useParams()
    const navigate = useNavigate()
    const [product, setProduct] = useState(null)
    const [loading, setLoading] = useState(true)
    const [error, setError] = useState('')
    const sanitizedDescription = useMemo(
        () => sanitizeRichTextHtml(product?.description || 'No description provided.'),
        [product?.description],
    )

    useEffect(() => {
        const fetchProduct = async () => {
            try {
                const response = await axios.get(`/api/public/affiliate/products/${slug}`)
                setProduct(response.data)
            } catch (err) {
                console.error('[AffiliateProductDetail] Failed to fetch product:', err)
                setError('Failed to load product details.')
            } finally {
                setLoading(false)
            }
        }
        fetchProduct()
    }, [slug])

    const handlePromote = () => {
        if (!affiliateAuthApi.isAuthenticated()) {
            navigate('/affiliate/register')
        } else {
            navigate(`/affiliate/portal?productSlug=${slug}`)
        }
    }

    const priceLabel = product?.hasVariants
        ? product?.minVariantPrice == null
            ? '—'
            : product.maxVariantPrice != null && product.maxVariantPrice > product.minVariantPrice
                ? `${product.minVariantPrice} - ${product.maxVariantPrice} ${product.currency}`
                : `${product.minVariantPrice} ${product.currency}`
        : product?.price != null
            ? `${product.price} ${product.currency}`
            : '—'

    if (loading) return (
        <div style={styles.loadingContainer}>
            <div style={styles.spinner} />
            <p style={styles.loadingText}>Loading product details...</p>
        </div>
    )

    if (error || !product) return (
        <div style={styles.errorContainer}>
            <div style={styles.errorBanner}>⚠️ {error || 'Product not found'}</div>
            <Link to="/affiliate" style={{ color: '#a5b4fc', marginTop: '20px', display: 'inline-block' }}>
                ← Back to Marketplace
            </Link>
        </div>
    )

    return (
        <div style={styles.page}>
            {/* Header matches Landing */}
            <header style={styles.header}>
                <div style={styles.headerContent}>
                    <div style={styles.logoSection}>
                        <div style={styles.logoIcon}>💰</div>
                        <span style={styles.logoText}>Affiliate Marketplace</span>
                    </div>
                    <div style={styles.headerActions}>
                        <Link to="/affiliate" style={styles.loginLink}>
                            Browse Products
                        </Link>
                    </div>
                </div>
            </header>

            <main style={styles.main}>
                <div style={styles.grid}>
                    {/* Gallery Section */}
                    <div style={styles.galleryCard}>
                        {product.thumbnail ? (
                            <img src={product.thumbnail} alt={product.name} style={styles.mainImage} />
                        ) : (
                            <div style={styles.placeholderImage}>📦 No Image</div>
                        )}
                    </div>

                    {/* Product Info Section */}
                    <div style={styles.infoCard}>
                        <div style={styles.categoryBadge}>{product.category}</div>
                        <h1 style={styles.title}>{product.name}</h1>
                        <p style={styles.price}>{priceLabel}</p>

                        <div style={styles.storeDetails}>
                            Sold by <strong style={{ color: '#f1f5f9' }}>{product.storeName}</strong>
                        </div>

                        <div style={styles.description}>
                            <h3 style={styles.descTitle}>About this product</h3>
                            <div dangerouslySetInnerHTML={{ __html: sanitizedDescription }} />
                        </div>

                        <button onClick={handlePromote} style={styles.promoteButton}>
                            Promote 🚀
                        </button>

                        <div style={{ marginTop: '16px', fontSize: '14px', color: '#64748b', textAlign: 'center' }}>
                            Earn commission on every sale you refer.
                        </div>
                    </div>
                </div>
            </main>
        </div>
    )
}

const styles = {
    page: {
        fontFamily: "'Inter', -apple-system, system-ui, sans-serif",
        background: 'linear-gradient(135deg, #0f0f1a 0%, #1a1a2e 50%, #16213e 100%)',
        minHeight: '100vh',
        color: '#e2e8f0',
    },
    header: {
        borderBottom: '1px solid rgba(255, 255, 255, 0.06)',
        backdropFilter: 'blur(20px)', background: 'rgba(15, 15, 26, 0.6)',
    },
    headerContent: {
        maxWidth: '1200px', margin: '0 auto', padding: '16px 24px', display: 'flex', justifyContent: 'space-between', alignItems: 'center',
    },
    logoSection: { display: 'flex', alignItems: 'center', gap: '10px' },
    logoIcon: { fontSize: '28px' },
    logoText: {
        fontSize: '20px', fontWeight: 700, background: 'linear-gradient(135deg, #818cf8, #c084fc)',
        WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent',
    },
    headerActions: { display: 'flex', gap: '12px' },
    loginLink: {
        padding: '8px 20px', borderRadius: '10px', background: 'rgba(99, 102, 241, 0.15)',
        color: '#a5b4fc', textDecoration: 'none', fontWeight: 500, fontSize: '14px',
    },
    main: {
        maxWidth: '1200px',
        margin: '40px auto',
        padding: '0 24px',
    },
    grid: {
        display: 'grid',
        gridTemplateColumns: 'minmax(300px, 1fr) minmax(300px, 1fr)',
        gap: '40px',
        alignItems: 'start',
    },
    galleryCard: {
        background: 'rgba(255, 255, 255, 0.03)',
        borderRadius: '24px',
        border: '1px solid rgba(255, 255, 255, 0.06)',
        padding: '24px',
        aspectRatio: '1',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        overflow: 'hidden'
    },
    mainImage: {
        maxWidth: '100%',
        maxHeight: '100%',
        objectFit: 'contain',
        borderRadius: '12px'
    },
    placeholderImage: {
        fontSize: '48px',
        color: '#475569'
    },
    infoCard: {
        background: 'rgba(255, 255, 255, 0.03)',
        borderRadius: '24px',
        border: '1px solid rgba(255, 255, 255, 0.06)',
        padding: '40px',
    },
    categoryBadge: {
        display: 'inline-block',
        padding: '6px 16px',
        background: 'rgba(99, 102, 241, 0.1)',
        color: '#a5b4fc',
        borderRadius: '20px',
        fontSize: '14px',
        fontWeight: 'bold',
        marginBottom: '16px'
    },
    title: {
        fontSize: '32px',
        fontWeight: 800,
        margin: '0 0 16px 0',
        color: '#f1f5f9'
    },
    price: {
        fontSize: '28px',
        fontWeight: '700',
        color: '#a5b4fc',
        margin: '0 0 24px 0'
    },
    storeDetails: {
        padding: '16px',
        background: 'rgba(255, 255, 255, 0.05)',
        borderRadius: '12px',
        color: '#94a3b8',
        fontSize: '16px',
        marginBottom: '24px'
    },
    description: {
        color: '#cbd5e1',
        lineHeight: '1.6',
        fontSize: '16px',
        marginBottom: '40px'
    },
    descTitle: {
        fontSize: '18px',
        color: '#f1f5f9',
        borderBottom: '1px solid rgba(255, 255, 255, 0.1)',
        paddingBottom: '8px',
        marginBottom: '16px'
    },
    promoteButton: {
        width: '100%',
        padding: '16px 24px',
        borderRadius: '16px',
        background: 'linear-gradient(135deg, #6366f1, #8b5cf6)',
        color: '#fff',
        border: 'none',
        fontSize: '18px',
        fontWeight: 'bold',
        cursor: 'pointer',
        boxShadow: '0 8px 30px rgba(99, 102, 241, 0.3)',
        transition: 'transform 0.2s ease',
    },
    loadingContainer: {
        display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', height: '100vh', background: '#0f0f1a'
    },
    spinner: {
        width: '50px', height: '50px', border: '3px solid rgba(99, 102, 241, 0.2)', borderTopColor: '#818cf8', borderRadius: '50%', animation: 'spin 1s linear infinite'
    },
    loadingText: { color: '#64748b', marginTop: '16px', fontSize: '16px' },
    errorContainer: {
        display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', height: '100vh', background: '#0f0f1a', color: '#f1f5f9'
    },
    errorBanner: {
        padding: '20px', background: 'rgba(239, 68, 68, 0.1)', color: '#fca5a5', borderRadius: '12px', fontSize: '18px'
    }
}

export default AffiliateProductDetail
